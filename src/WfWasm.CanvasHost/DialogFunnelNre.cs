using System.Collections.Concurrent;

namespace WfWasm.CanvasHost;

// WC2315-runtime-nre: host-funnel-only File>New NRE instrumentation.
//
// The caught File>New dialog NREs (show/destroy/click funnel) never escape to
// the host's catch sites, so the click-handler reports never see them and the
// FirstChance hook only counted them. This recorder captures, at the host
// funnel, the NRE's throwing site + inner chain for exactly the window in
// which a host dialog action is in flight (ToolStrip/menu item click,
// modal click). Fork-side frames stay vendor-owned: nothing here resolves
// stack text (hostile frames abort the browser runtime), touches fork types,
// or edits fork files. Bounded by FirstChanceLogGate per-signature caps and
// a 32-entry ring. Fail-closed throughout: recording never throws.
public static class DialogFunnelNre
{
    public readonly struct FunnelEntry
    {
        public FunnelEntry(string breadcrumb, string site, string chain, string messageHead, string utc)
        {
            Breadcrumb = breadcrumb;
            Site = site;
            Chain = chain;
            MessageHead = messageHead;
            Utc = utc;
        }

        public string Breadcrumb { get; }
        public string Site { get; }
        public string Chain { get; }
        public string MessageHead { get; }
        public string Utc { get; }
    }

    private const int RingCap = 32;
    private const int MaxChainDepth = 3;
    private const int MaxHeadChars = 160;

    private static readonly ConcurrentQueue<FunnelEntry> s_ring = new();

    [ThreadStatic]
    private static string? s_crumb;

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Scope(string? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            s_crumb = _previous;
        }
    }

    public static IDisposable Enter(string crumb)
    {
        string? previous = s_crumb;
        s_crumb = crumb;
        return new Scope(previous);
    }

    public static bool TryRecord(Exception? ex)
    {
        try
        {
            string? crumb = s_crumb;
            if (string.IsNullOrEmpty(crumb) || ex is not NullReferenceException nre)
                return false;
            string site = SafeSite(nre);
            string msgHead = Head(nre.Message);
            string chain = SafeChain(nre);
            string key = "nre-funnel|" + site + "|" + msgHead;
            if (!FirstChanceLogGate.ShouldLog(key))
                return false;
            var entry = new FunnelEntry(crumb, site, chain, msgHead,
                DateTime.UtcNow.ToString("HH:mm:ss.fff"));
            s_ring.Enqueue(entry);
            while (s_ring.Count > RingCap && s_ring.TryDequeue(out _))
            {
            }
            Console.WriteLine($"[NRE-FUNNEL] crumb={crumb} site={site} chain={chain} msg={msgHead}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static FunnelEntry[] TakeFunnelNres()
    {
        var list = new List<FunnelEntry>();
        while (s_ring.TryDequeue(out FunnelEntry entry))
            list.Add(entry);
        return list.ToArray();
    }

    private static string SafeSite(Exception ex)
    {
        try
        {
            var target = ex.TargetSite;
            if (target is null)
                return "";
            var type = target.DeclaringType;
            if (type is null)
                return "";
            return type.FullName + "." + target.Name;
        }
        catch
        {
            return "";
        }
    }

    private static string SafeChain(Exception ex)
    {
        try
        {
            var parts = new List<string>();
            var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance) { ex };
            int depth = 0;
            for (Exception? inner = ex.InnerException;
                inner != null && depth < MaxChainDepth && seen.Add(inner);
                inner = inner.InnerException)
            {
                depth++;
                string innerSite = SafeSite(inner);
                parts.Add(innerSite.Length == 0
                    ? inner.GetType().Name + ": " + Head(inner.Message)
                    : inner.GetType().Name + "@" + innerSite + ": " + Head(inner.Message));
            }
            return parts.Count == 0 ? "-" : string.Join(" <- ", parts);
        }
        catch
        {
            return "-";
        }
    }

    private static string Head(string? text)
    {
        try
        {
            string line = (text ?? "").Split('\n')[0].Trim().Replace('\r', ' ');
            return line.Length > MaxHeadChars ? line.Substring(0, MaxHeadChars) : line;
        }
        catch
        {
            return "";
        }
    }
}
