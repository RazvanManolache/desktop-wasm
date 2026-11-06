using System.Collections.Generic;

namespace System.Text;

// PUSH-1106-vendor: real ISO-8859-16 (codepage 28606) for SubtitleEdit
// startup. The inbox CodePages provider does not supply 28606, so
// WasmEncodingBoot registers this narrow provider additively.
// Decode table: full 256 bytes from the local Python stdlib
// iso8859_16 codec (oracle dump 2026-11-06; all bytes defined, ASCII
// identity). Single-byte, BMP-only; unmappable encode input honors
// EncoderFallback (replacement '?' by default, strict throws).
public sealed class Iso885916Encoding : Encoding
{
    internal const int CodePageNumber = 28606;

    private static readonly char[] DecodeMap = new char[256]
    {
        '\u0000', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\u0007',
        '\u0008', '\u0009', '\u000A', '\u000B', '\u000C', '\u000D', '\u000E', '\u000F',
        '\u0010', '\u0011', '\u0012', '\u0013', '\u0014', '\u0015', '\u0016', '\u0017',
        '\u0018', '\u0019', '\u001A', '\u001B', '\u001C', '\u001D', '\u001E', '\u001F',
        '\u0020', '\u0021', '\u0022', '\u0023', '\u0024', '\u0025', '\u0026', '\u0027',
        '\u0028', '\u0029', '\u002A', '\u002B', '\u002C', '\u002D', '\u002E', '\u002F',
        '\u0030', '\u0031', '\u0032', '\u0033', '\u0034', '\u0035', '\u0036', '\u0037',
        '\u0038', '\u0039', '\u003A', '\u003B', '\u003C', '\u003D', '\u003E', '\u003F',
        '\u0040', '\u0041', '\u0042', '\u0043', '\u0044', '\u0045', '\u0046', '\u0047',
        '\u0048', '\u0049', '\u004A', '\u004B', '\u004C', '\u004D', '\u004E', '\u004F',
        '\u0050', '\u0051', '\u0052', '\u0053', '\u0054', '\u0055', '\u0056', '\u0057',
        '\u0058', '\u0059', '\u005A', '\u005B', '\u005C', '\u005D', '\u005E', '\u005F',
        '\u0060', '\u0061', '\u0062', '\u0063', '\u0064', '\u0065', '\u0066', '\u0067',
        '\u0068', '\u0069', '\u006A', '\u006B', '\u006C', '\u006D', '\u006E', '\u006F',
        '\u0070', '\u0071', '\u0072', '\u0073', '\u0074', '\u0075', '\u0076', '\u0077',
        '\u0078', '\u0079', '\u007A', '\u007B', '\u007C', '\u007D', '\u007E', '\u007F',
        '\u0080', '\u0081', '\u0082', '\u0083', '\u0084', '\u0085', '\u0086', '\u0087',
        '\u0088', '\u0089', '\u008A', '\u008B', '\u008C', '\u008D', '\u008E', '\u008F',
        '\u0090', '\u0091', '\u0092', '\u0093', '\u0094', '\u0095', '\u0096', '\u0097',
        '\u0098', '\u0099', '\u009A', '\u009B', '\u009C', '\u009D', '\u009E', '\u009F',
        '\u00A0', '\u0104', '\u0105', '\u0141', '\u20AC', '\u201E', '\u0160', '\u00A7',
        '\u0161', '\u00A9', '\u0218', '\u00AB', '\u0179', '\u00AD', '\u017A', '\u017B',
        '\u00B0', '\u00B1', '\u010C', '\u0142', '\u017D', '\u201D', '\u00B6', '\u00B7',
        '\u017E', '\u010D', '\u0219', '\u00BB', '\u0152', '\u0153', '\u0178', '\u017C',
        '\u00C0', '\u00C1', '\u00C2', '\u0102', '\u00C4', '\u0106', '\u00C6', '\u00C7',
        '\u00C8', '\u00C9', '\u00CA', '\u00CB', '\u00CC', '\u00CD', '\u00CE', '\u00CF',
        '\u0110', '\u0143', '\u00D2', '\u00D3', '\u00D4', '\u0150', '\u00D6', '\u015A',
        '\u0170', '\u00D9', '\u00DA', '\u00DB', '\u00DC', '\u0118', '\u021A', '\u00DF',
        '\u00E0', '\u00E1', '\u00E2', '\u0103', '\u00E4', '\u0107', '\u00E6', '\u00E7',
        '\u00E8', '\u00E9', '\u00EA', '\u00EB', '\u00EC', '\u00ED', '\u00EE', '\u00EF',
        '\u0111', '\u0144', '\u00F2', '\u00F3', '\u00F4', '\u0151', '\u00F6', '\u015B',
        '\u0171', '\u00F9', '\u00FA', '\u00FB', '\u00FC', '\u0119', '\u021B', '\u00FF',
    };

    private static readonly Dictionary<char, byte> EncodeMap = BuildEncodeMap();

    private static Dictionary<char, byte> BuildEncodeMap()
    {
        var map = new Dictionary<char, byte>(256);
        for (int i = 0; i < 256; i++)
            map[(char)i] = (byte)i;
        for (int i = 128; i < 256; i++)
            map[DecodeMap[i]] = (byte)i;
        return map;
    }

    public Iso885916Encoding()
        : base(CodePageNumber)
    {
    }

    public Iso885916Encoding(EncoderFallback encoderFallback, DecoderFallback decoderFallback)
        : base(CodePageNumber, encoderFallback, decoderFallback)
    {
    }

    public override string EncodingName => "Latin Alphabet No. 10";
    public override string WebName => "iso-8859-16";
    public override string BodyName => "iso-8859-16";
    public override string HeaderName => "iso-8859-16";
    public override bool IsSingleByte => true;

    public override int GetByteCount(char[] chars, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (chars.Length - index < count)
            throw new ArgumentOutOfRangeException(nameof(count));
        return EncodeChars(chars, index, count, null, 0);
    }

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(charCount);
        ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
        if (chars.Length - charIndex < charCount)
            throw new ArgumentOutOfRangeException(nameof(charCount));
        if (bytes.Length - byteIndex < GetByteCount(chars, charIndex, charCount))
            throw new ArgumentException("Insufficient space in target buffer.", nameof(bytes));
        return EncodeChars(chars, charIndex, charCount, bytes, byteIndex);
    }

    // Shared encode core: counts when bytes is null, writes otherwise.
    // Falls back per unit (surrogate pairs together); fallback output
    // that is itself unencodable re-enters the fallback, so custom
    // fallbacks terminate exactly like the framework single-byte
    // encodings (replacement ends at '?'; exception fallback throws).
    private int EncodeChars(char[] chars, int index, int count, byte[]? bytes, int byteIndex)
    {
        EncoderFallbackBuffer? fallback = null;
        int end = index + count;
        int bi = byteIndex;
        void Emit(byte b)
        {
            if (bytes is null)
            {
                bi++;
                return;
            }
            bytes[bi++] = b;
        }
        void EmitFallbackChars(char[] src, int at, int n)
        {
            fallback ??= EncoderFallback.CreateFallbackBuffer();
            fallback.Reset();
            bool applied;
            if (n == 2)
                applied = fallback.Fallback(src[at], src[at + 1], at);
            else
                applied = fallback.Fallback(src[at], at);
            if (!applied)
            {
                throw new EncoderFallbackException(
                    $"Cannot encode input at offset {at} in ISO-8859-16.");
            }
            while (fallback.Remaining > 0)
                EmitOne(fallback.GetNextChar(), src, at, n);
        }
        void EmitOne(char c, char[] src, int at, int n)
        {
            if (c <= 0x7F)
            {
                Emit((byte)c);
                return;
            }
            if (EncodeMap.TryGetValue(c, out byte b))
            {
                Emit(b);
                return;
            }
            EmitFallbackChars(new[] { c }, 0, 1);
        }
        for (int i = index; i < end; i++)
        {
            char c = chars[i];
            if (c <= 0x7F)
            {
                Emit((byte)c);
                continue;
            }
            if (EncodeMap.TryGetValue(c, out byte b))
            {
                Emit(b);
                continue;
            }
            if (char.IsHighSurrogate(c) && i + 1 < end && char.IsLowSurrogate(chars[i + 1]))
            {
                EmitFallbackChars(chars, i, 2);
                i++;
                continue;
            }
            EmitFallbackChars(chars, i, 1);
        }
        return bi - byteIndex;
    }

    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (bytes.Length - index < count)
            throw new ArgumentOutOfRangeException(nameof(count));
        return count;
    }

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
        if (bytes.Length - byteIndex < byteCount)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        if (chars.Length - charIndex < byteCount)
            throw new ArgumentException("Insufficient space in target buffer.", nameof(chars));
        for (int i = 0; i < byteCount; i++)
            chars[charIndex + i] = DecodeMap[bytes[byteIndex + i]];
        return byteCount;
    }

    public override int GetMaxByteCount(int charCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(charCount);
        long max = (long)charCount + EncoderFallback.MaxCharCount;
        return max > int.MaxValue ? int.MaxValue : (int)max;
    }

    public override int GetMaxCharCount(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        long max = (long)byteCount + DecoderFallback.MaxCharCount;
        return max > int.MaxValue ? int.MaxValue : (int)max;
    }
}

// Narrow provider answering only codepage 28606 / "iso-8859-16";
// every other lookup returns null so earlier providers keep theirs.
public sealed class Iso885916EncodingProvider : EncodingProvider
{
    public static readonly Iso885916EncodingProvider Instance = new();

    private Iso885916EncodingProvider()
    {
    }

    public override Encoding? GetEncoding(int codepage) =>
        codepage == Iso885916Encoding.CodePageNumber ? new Iso885916Encoding() : null;

    public override Encoding? GetEncoding(string name) =>
        string.Equals(name, "iso-8859-16", StringComparison.OrdinalIgnoreCase)
            ? new Iso885916Encoding() : null;

    public override IEnumerable<EncodingInfo> GetEncodings()
    {
        yield return new EncodingInfo(
            this, Iso885916Encoding.CodePageNumber, "iso-8859-16", "Latin Alphabet No. 10");
    }
}
