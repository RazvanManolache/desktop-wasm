extern alias WfDraw;

using System;
using System.Runtime.InteropServices;
using WfPrivateFonts = WfDraw::System.Drawing.Text.PrivateFontCollection;
using Xunit;

namespace WfWasm.Drawing.Tests;

// Private-font byte registry (browser FontFace bridge for vendor icon
// fonts): AddMemoryFont must record (parsed family name -> TTF bytes) so
// BrowserText can register the exact bytes when text first uses the family.
// Uses a hand-built minimal name table — no vendor binaries in the repo.
public sealed class PrivateFontRegistrationTests
{
    private static byte[] BuildNameTableTtf(string family)
    {
        byte[] nameBytes = System.Text.Encoding.BigEndianUnicode.GetBytes(family);
        // offset table (12) + 1 table record (16) + name header (6) + 1 record (12) + string
        byte[] buf = new byte[12 + 16 + 6 + 12 + nameBytes.Length];
        void U16(int o, int v) { buf[o] = (byte)(v >> 8); buf[o + 1] = (byte)v; }
        void U32(int o, int v) { buf[o] = (byte)(v >> 24); buf[o + 1] = (byte)(v >> 16); buf[o + 2] = (byte)(v >> 8); buf[o + 3] = (byte)v; }
        // sfnt version 1.0, 1 table
        U32(0, 0x00010000); U16(4, 1);
        // 'name' record at offset 28
        buf[12] = (byte)'n'; buf[13] = (byte)'a'; buf[14] = (byte)'m'; buf[15] = (byte)'e';
        U32(12 + 8, 28); U32(12 + 12, 6 + 12 + nameBytes.Length);
        // name table: format 0, 1 record, storage offset 18
        U16(28, 0); U16(30, 1); U16(32, 18);
        // record: platform 3 (Windows), encoding 1 (UCS-2), lang 0x0409, nameID 1 (family)
        U16(34, 3); U16(36, 1); U16(38, 0x0409); U16(40, 1);
        U16(42, nameBytes.Length); U16(44, 0);
        Buffer.BlockCopy(nameBytes, 0, buf, 46, nameBytes.Length);
        return buf;
    }

    private static void AddBytes(WfPrivateFonts pfc, byte[] bytes)
    {
        IntPtr mem = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, mem, bytes.Length);
            pfc.AddMemoryFont(mem, bytes.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    [Fact]
    public void AddMemoryFont_Records_Family_And_Bytes()
    {
        const string family = "TestPrivateIconsWF";
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, BuildNameTableTtf(family));
        Assert.Contains(pfc.Families, f => f.Name == family);
        Assert.True(WfDraw::System.Drawing.Text.PrivateFontCollection.TryGetPrivateFontBytes(family, out byte[]? got));
        Assert.NotNull(got);
        Assert.Equal(BuildNameTableTtf(family), got);
    }

    [Fact]
    public void AddMemoryFont_Garbage_Records_Nothing()
    {
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, new byte[64]);
        Assert.Empty(pfc.Families);
        Assert.False(WfDraw::System.Drawing.Text.PrivateFontCollection.TryGetPrivateFontBytes("NoSuchFamilyXYZ", out _));
    }

    // ReaLTaiizor Form17 retry gate (M4): vendor code Dictionary.Add()s
    // every Families name, so a repeated add must not duplicate the
    // family (desktop GDI+ lists each family once). Same file twice.
    [Fact]
    public void AddFontFile_Dedupes_Same_File()
    {
        using var pfc = new WfPrivateFonts();
        pfc.AddFontFile("Roboto-Regular.ttf");
        pfc.AddFontFile("Roboto-Regular.ttf");
        Assert.Single(pfc.Families);
        Assert.Equal("Roboto-Regular", pfc.Families[0].Name);
    }

    // Same memory font twice: parsed name must appear once.
    [Fact]
    public void AddMemoryFont_Dedupes_Same_Family()
    {
        const string family = "TestDedupeIcons";
        byte[] ttf = BuildNameTableTtf(family);
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, ttf);
        AddBytes(pfc, ttf);
        Assert.Single(pfc.Families);
        Assert.Equal(family, pfc.Families[0].Name);
    }

    // Cross-path gate case: file stem colliding with a memory font name
    // must collapse to one family, so the vendor Dictionary.Add loop
    // over Families cannot throw on the duplicate key.
    [Fact]
    public void AddFontFile_And_MemoryFont_Collapse_To_Vendor_Safe_Set()
    {
        const string family = "Roboto";
        byte[] ttf = BuildNameTableTtf(family);
        using var pfc = new WfPrivateFonts();
        pfc.AddFontFile(family + ".ttf");
        AddBytes(pfc, ttf);
        pfc.AddFontFile(family + ".ttf");
        var names = new System.Collections.Generic.List<string>();
        foreach (var f in pfc.Families)
            names.Add(f.Name);
        Assert.Single(names);
        var vendor = new System.Collections.Generic.Dictionary<string, int>();
        foreach (string n in names)
            vendor.Add(n, 1);
        Assert.Single(vendor);
    }

    // Multi-record table like real fonts carry (storage follows ALL
    // records: offset = 6 + 12 * count). Verifies the Windows Unicode
    // US-English family wins over Mac Roman, other languages and other
    // name orderings.
    private static byte[] BuildNameTableTtf(params (int platform, int encoding, int language, byte[] blob)[] records)
    {
        int n = records.Length;
        int storageBase = 6 + 12 * n;
        int totalStrings = 0;
        foreach (var r in records)
            totalStrings += r.blob.Length;
        byte[] buf = new byte[12 + 16 + storageBase + totalStrings];
        void U16(int o, int v) { buf[o] = (byte)(v >> 8); buf[o + 1] = (byte)v; }
        void U32(int o, int v) { buf[o] = (byte)(v >> 24); buf[o + 1] = (byte)(v >> 16); buf[o + 2] = (byte)(v >> 8); buf[o + 3] = (byte)v; }
        U32(0, 0x00010000); U16(4, 1);
        buf[12] = (byte)'n'; buf[13] = (byte)'a'; buf[14] = (byte)'m'; buf[15] = (byte)'e';
        U32(20, 28); U32(24, storageBase + totalStrings);
        U16(28, 0); U16(30, n); U16(32, storageBase);
        int storage = 0;
        for (int k = 0; k < n; k++)
        {
            int r = 34 + k * 12;
            U16(r, records[k].platform); U16(r + 2, records[k].encoding); U16(r + 4, records[k].language); U16(r + 6, 1);
            U16(r + 8, records[k].blob.Length); U16(r + 10, storage);
            Buffer.BlockCopy(records[k].blob, 0, buf, 28 + storageBase + storage, records[k].blob.Length);
            storage += records[k].blob.Length;
        }
        return buf;
    }

    private static byte[] Uni(string s) => System.Text.Encoding.BigEndianUnicode.GetBytes(s);

    // Empty US-English record must not shadow the real family name.
    [Fact]
    public void AddMemoryFont_EmptyUsEnglishRecord_Skipped()
    {
        byte[] ttf = BuildNameTableTtf((3, 1, 0x0409, Array.Empty<byte>()), (3, 1, 0x0409, Uni("RealFamily")));
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, ttf);
        Assert.Equal(new[] { "RealFamily" }, pfc.Families.Select(f => f.Name));
    }

    // An all-empty name table is an unnamed font: fail-closed, no
    // empty-named family (two such fonts must not Dictionary-collide).
    [Fact]
    public void AddMemoryFont_EmptyOnlyRecord_RecordsNothing()
    {
        byte[] ttf = BuildNameTableTtf((3, 1, 0x0409, Array.Empty<byte>()));
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, ttf);
        Assert.Empty(pfc.Families);
    }

    // Realistic record mix: Mac Roman + French + US-English family.
    [Fact]
    public void AddMemoryFont_MultiRecordTable_Selects_UsEnglishFamily()
    {
        byte[] ttf = BuildNameTableTtf(
            (1, 0, 0, System.Text.Encoding.Latin1.GetBytes("MacFam")),
            (3, 1, 0x040C, Uni("FamFr")),
            (3, 1, 0x0409, Uni("RealFam")));
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, ttf);
        Assert.Equal(new[] { "RealFam" }, pfc.Families.Select(f => f.Name));
    }

    // Same family name, different bytes: one family; the process byte
    // registry keeps the first bytes (stable identity, bounded).
    [Fact]
    public void AddMemoryFont_SameName_DifferentBytes_RegistryKeepsFirst()
    {
        const string family = "TestDupBytes";
        byte[] first = BuildNameTableTtf((3, 1, 0x0409, Uni(family)));
        byte[] second = BuildNameTableTtf((1, 0, 0, System.Text.Encoding.Latin1.GetBytes(family)));
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, first);
        AddBytes(pfc, second);
        Assert.Single(pfc.Families);
        Assert.True(WfDraw::System.Drawing.Text.PrivateFontCollection.TryGetPrivateFontBytes(family, out byte[]? got));
        Assert.Equal(first, got);
    }

    // Disposal clears the instance but must not drop the shared
    // process-wide byte registry other collections may rely on.
    [Fact]
    public void Dispose_Clears_Instance_But_Retains_Shared_Registry()
    {
        const string family = "TestDisposeKeepsBytes";
        byte[] ttf = BuildNameTableTtf((3, 1, 0x0409, Uni(family)));
        var pfc = new WfPrivateFonts();
        AddBytes(pfc, ttf);
        pfc.Dispose();
        Assert.Empty(pfc.Families);
        pfc.Dispose();
        Assert.Empty(pfc.Families);
        Assert.True(WfDraw::System.Drawing.Text.PrivateFontCollection.TryGetPrivateFontBytes(family, out byte[]? got));
        Assert.Equal(ttf, got);
        pfc.Dispose();
    }

    [Fact]
    public void AddMemoryFont_MacRoman_Name_Parses()
    {
        // platform 1 / encoding 0 / lang 0 Mac Roman family record
        byte[] nameBytes = System.Text.Encoding.Latin1.GetBytes("TestMacIcons");
        byte[] buf = new byte[12 + 16 + 6 + 12 + nameBytes.Length];
        void U16(int o, int v) { buf[o] = (byte)(v >> 8); buf[o + 1] = (byte)v; }
        void U32(int o, int v) { buf[o] = (byte)(v >> 24); buf[o + 1] = (byte)(v >> 16); buf[o + 2] = (byte)(v >> 8); buf[o + 3] = (byte)v; }
        U32(0, 0x00010000); U16(4, 1);
        buf[12] = (byte)'n'; buf[13] = (byte)'a'; buf[14] = (byte)'m'; buf[15] = (byte)'e';
        U32(20, 28); U32(24, 6 + 12 + nameBytes.Length);
        U16(28, 0); U16(30, 1); U16(32, 18);
        U16(34, 1); U16(36, 0); U16(38, 0); U16(40, 1);
        U16(42, nameBytes.Length); U16(44, 0);
        Buffer.BlockCopy(nameBytes, 0, buf, 46, nameBytes.Length);
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, buf);
        Assert.Contains(pfc.Families, f => f.Name == "TestMacIcons");
    }
}
