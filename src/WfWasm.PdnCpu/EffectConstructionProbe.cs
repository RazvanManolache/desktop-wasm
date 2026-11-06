namespace WfWasm.PdnCpu;

// Fork-side construction diagnostic for the resourcecache/rewriter bridge
// (WC2315 section 4, vendor half): records WHO asked for a resource
// (caller site), WHAT key was requested (resource-key type name), WHETHER
// the cache held it, and WHERE it threw (throwing-site type+method plus an
// inner-exception chain). Never reads StackTrace or Exception.ToString
// (same browser-hostile-frame rule as the host DialogFunnelNre contract),
// never throws, console-gated for UTC-ms correlation with host
// [NRE-FUNNEL] crumbs, and ring-buffered with TakeDrained for tests and
// the future rewriter call-site probes (RelayCreateConfigForm ENTER/EXIT,
// ResourceCacheExtensions.GetCachedOrCreateResource miss/NRE).
public static class EffectConstructionProbe
{
    public sealed class ConstructionNote
    {
        public string UtcMs = "";
        public string Stage = "";
        public string ResourceKey = "";
        public bool? CacheHit;
        public string CallerSite = "";
        public string ThrowSite = "";
        public string Chain = "";
        public string MessageHead = "";
    }

    private const int RingCap = 32;
    private const int ConsoleCap = 200;
    private const int MessageHeadCap = 160;
    private const int ChainDepthCap = 3;

    private static readonly object s_gate = new();
    private static readonly System.Collections.Generic.Queue<ConstructionNote> s_ring = new();
    private static int s_consoleCount;

    private static string SafeText(string? value)
    {
        try { return value ?? ""; }
        catch { return ""; }
    }

    private static string SafeMessage(Exception ex)
    {
        try { return ex.Message ?? ""; }
        catch { return "<message unreadable: " + ex.GetType().FullName + ">"; }
    }

    private static string Head(string value, int cap)
    {
        try
        {
            if (value.Length > cap) return value.Substring(0, cap);
            return value;
        }
        catch { return ""; }
    }

    private static string SiteOf(Exception fault)
    {
        try
        {
            var site = fault.TargetSite;
            if (site is null) return "";
            string type;
            try { type = site.DeclaringType?.FullName ?? site.DeclaringType?.Name ?? ""; }
            catch { type = ""; }
            string name;
            try { name = site.Name ?? ""; }
            catch { name = ""; }
            if (type == "" && name == "") return "";
            return type == "" ? name : type + "." + name;
        }
        catch { return ""; }
    }

    private static string ChainOf(Exception fault)
    {
        try
        {
            var parts = new System.Collections.Generic.List<string>();
            Exception? cur = fault;
            int depth = 0;
            while (cur is not null && depth < ChainDepthCap)
            {
                string type;
                try { type = cur.GetType().FullName ?? cur.GetType().Name; }
                catch { type = "<type unreadable>"; }
                parts.Add(type + ": " + Head(SafeMessage(cur), MessageHeadCap));
                try { cur = cur.InnerException; }
                catch { cur = null; }
                depth++;
            }
            return string.Join(" <- ", parts);
        }
        catch { return ""; }
    }

    public static bool Note(
        string? stage,
        string? resourceKey,
        bool? cacheHit,
        string? callerSite,
        Exception? fault)
    {
        if (string.IsNullOrEmpty(stage)) return false;
        try
        {
            var note = new ConstructionNote
            {
                UtcMs = DateTime.UtcNow.ToString("HH:mm:ss.fff"),
                Stage = stage,
                ResourceKey = SafeText(resourceKey),
                CacheHit = cacheHit,
                CallerSite = SafeText(callerSite),
            };
            if (fault is not null)
            {
                note.ThrowSite = SiteOf(fault);
                note.Chain = ChainOf(fault);
                note.MessageHead = Head(SafeMessage(fault), MessageHeadCap);
            }
            string hit = cacheHit is null ? "?" : (cacheHit.Value ? "1" : "0");
            if (System.Threading.Interlocked.Increment(ref s_consoleCount) <= ConsoleCap)
            {
                try
                {
                    System.Console.WriteLine(
                        "[FXBUILD " + note.UtcMs
                        + " stage=" + note.Stage
                        + " key=" + note.ResourceKey
                        + " hit=" + hit
                        + " caller=" + note.CallerSite
                        + " site=" + note.ThrowSite
                        + " chain=" + note.Chain
                        + " msg=" + note.MessageHead + "]");
                }
                catch { }
            }
            lock (s_gate)
            {
                while (s_ring.Count >= RingCap) s_ring.Dequeue();
                s_ring.Enqueue(note);
            }
            return true;
        }
        catch { return false; }
    }

    public static bool NoteMiss(string? stage, string? resourceKey)
    {
        return Note(stage, resourceKey, false, null, null);
    }

    public static ConstructionNote[] TakeDrained()
    {
        lock (s_gate)
        {
            var notes = s_ring.ToArray();
            s_ring.Clear();
            return notes;
        }
    }
}
