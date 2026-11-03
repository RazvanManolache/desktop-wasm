namespace WfWasm.PdnCpu;
public static unsafe partial class CpuPaint
{
    // Portable natural cubic interpolation for the legacy 0..255 curve
    // control points. The same table drives the graph and CPU transfer.
    public static byte[][]? CurveTables(object tokenOrControl)
    {
        var curves = MostDerivedProperty(tokenOrControl.GetType(), "ControlPoints")?.GetValue(tokenOrControl) as SortedList<int, int>[];
        if (curves is null || curves.Length is not (1 or 3)) return null;
        return curves.Select(CurveTable).ToArray();
    }

    private static byte[] CurveTable(SortedList<int, int> points)
    {
        if (points.Count < 2) throw new ArgumentException("A curve requires two distinct points.");
        int n = points.Count; double[] second = new double[n], work = new double[n];
        for (int i = 1; i < n - 1; i++)
        {
            double left = points.Keys[i] - points.Keys[i - 1], right = points.Keys[i + 1] - points.Keys[i];
            double ratio = left / (left + right), pivot = ratio * second[i - 1] + 2;
            second[i] = (ratio - 1) / pivot;
            double slopeChange = (points.Values[i + 1] - points.Values[i]) / right - (points.Values[i] - points.Values[i - 1]) / left;
            work[i] = (6 * slopeChange / (left + right) - ratio * work[i - 1]) / pivot;
        }
        for (int i = n - 2; i >= 0; i--) second[i] = second[i] * second[i + 1] + work[i];
        byte[] table = new byte[256]; int segment = 0;
        for (int x = 0; x < 256; x++)
        {
            while (segment < n - 2 && x > points.Keys[segment + 1]) segment++;
            double width = points.Keys[segment + 1] - points.Keys[segment];
            double a = (points.Keys[segment + 1] - x) / width, b = (x - points.Keys[segment]) / width;
            double y = a * points.Values[segment] + b * points.Values[segment + 1]
                + ((a * a * a - a) * second[segment] + (b * b * b - b) * second[segment + 1]) * width * width / 6;
            table[x] = (byte)Math.Clamp((int)Math.Round(y), 0, 255);
        }
        return table;
    }

    private static bool RenderTransferTileCpu(object owner, object dst, object point)
    {
        if (!s_tileSources.TryGetValue(owner, out var source)) return false;
        var token = GetEffectToken(owner); if (token is null) return false;
        bool curves = owner.GetType().Name.Contains("Curves", StringComparison.Ordinal);
        byte[][]? tables = curves ? CurveTables(token) : null;
        if (curves && tables is null) return false;
        int[] levels = new int[4]; bool[] enabled = new bool[4];
        string[] names = { "Blue", "Green", "Red", "Alpha" };
        if (!curves)
            for (int c = 0; c < 4; c++)
            {
                levels[c] = Math.Clamp(GetTokenIntProperty(token, names[c] + "Levels", 16), 2, 64);
                enabled[c] = Convert.ToBoolean(GetTokenValue(token, names[c] + "Enable") ?? true);
            }
        string key = curves ? "curve:" + string.Join("/", tables!.Select(Convert.ToHexString))
            : "posterize:" + string.Join(",", levels) + ":" + string.Join(",", enabled);
        var cached = s_spatial.GetValue(source, _ => new SpatialResult()); byte[] output;
        lock (cached.Gate)
        {
            if (cached.Key != key)
            {
                output = TightPixels(source);
                for (int i = 0; i < output.Length; i += 4)
                {
                    if (!curves)
                    {
                        for (int c = 0; c < 4; c++)
                            if (enabled[c]) output[i + c] = (byte)Math.Clamp((int)Math.Round(Math.Round(output[i + c] * (levels[c] - 1) / 255.0) * 255.0 / (levels[c] - 1)), 0, 255);
                    }
                    else if (tables!.Length == 3)
                    {
                        for (int c = 0; c < 3; c++) output[i + c] = tables[2 - c][output[i + c]];
                    }
                    else
                    {
                        int lum = Math.Clamp((int)Math.Round(output[i + 2] * 0.2126 + output[i + 1] * 0.7152 + output[i] * 0.0722), 0, 255);
                        int delta = tables[0][lum] - lum;
                        for (int c = 0; c < 3; c++) output[i + c] = (byte)Math.Clamp(output[i + c] + delta, 0, 255);
                    }
                }
                cached.Pixels = output; cached.Key = key;
            }
            output = cached.Pixels;
        }
        return CopyEffectTile(source, output, dst, point);
    }
}
