using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Principal;

// Slice-30 platform shims (ScreenToGif prerequisites, shim, not port).
//
// The upstream build output never contains these shared-framework/desktop-only
// assemblies, so every live reference to them is a package-level
// "missing-dependency" gap: 2x System.Windows.Forms (ScreenToGif, Native),
// System.Threading.AccessControl + System.Security.Cryptography.ProtectedData
// (Util), System.CodeDom (System.Management, single return-type position).
// This file carries the engine counterparts the converter rewrites those
// references onto; each is a live adapter over browser-session state, never
// Windows-native execution.
//
// Verified against live .NET on 2026-10-26 (Framework System.Windows.Forms
// for Keys/Timer/SystemInformation; net10 System.Threading.AccessControl for
// MutexRights; nuget System.Security.Cryptography.ProtectedData 9.0.0 and
// System.CodeDom 4.6.0 for DataProtectionScope/CodeTypeDeclaration):
// Keys carries all 194 real values incl. aliases (CapsLock=Capital=20,
// Enter=Return=13, OemSemicolon=Oem1=186), Timer.Interval <= 0 throws with
// default 100 and Enabled false, MouseWheelScrollDelta is 120,
// DoubleClickTime default is 500, MutexRights.FullControl is 2031617,
// DataProtectionScope.CurrentUser=0/LocalMachine=1, CodeTypeDeclaration has
// the () and (string) ctors over CodeTypeMember.
//
// Retained limits: Screen is the single session viewport (1024x768 pinned
// SystemParameters virtual screen; no multi-monitor, no taskbar inset, window
// handles do not resolve); Timer ticks on the dispatcher queue (no message
// pump); MutexSecurity is a retained rule store with no OS enforcement
// (named-mutex ACLs do not exist in the browser; single-instance scope is the
// session); ProtectedData keys are per-process random (ciphertext does not
// survive restart, unlike DPAPI user scope); CodeTypeDeclaration is a
// return-slot mirror (the WMI strong-type codegen path never executes in the
// browser: WMI queries throw first and Feedback renders its own designed
// "No processor found" rows).
// What still degrades: every GDI+/handle path stays a precise boundary —
// System.Drawing.Common (LockBits/Scan0 pixel pipelines, HDC/HBITMAP
// interop, codecs, metafiles) and the KGySoft.Drawing image pipeline built
// on it, so capture/frame-processing/export still fail conversion loudly.

namespace System.Windows.Forms {

/// <summary>Win32 window handle contract (matches System.Windows.Forms.IWin32Window).</summary>
public interface IWin32Window
{
    IntPtr Handle { get; }
}

/// <summary>
/// Key codes (matches System.Windows.Forms.Keys). All 194 real values
/// generated from live Framework reflection, aliases included, so the
/// Native hotkey helper's Enum.Parse(typeof(Keys), wpfKeyName) round-trips
/// exactly like WinForms; the parsed codes reach user32 only on Windows.
/// </summary>
[Flags]
public enum Keys
{
    A = 65,
    Add = 107,
    Alt = 262144,
    Apps = 93,
    Attn = 246,
    B = 66,
    Back = 8,
    BrowserBack = 166,
    BrowserFavorites = 171,
    BrowserForward = 167,
    BrowserHome = 172,
    BrowserRefresh = 168,
    BrowserSearch = 170,
    BrowserStop = 169,
    C = 67,
    Cancel = 3,
    Capital = 20,
    CapsLock = 20,
    Clear = 12,
    Control = 131072,
    ControlKey = 17,
    Crsel = 247,
    D = 68,
    D0 = 48,
    D1 = 49,
    D2 = 50,
    D3 = 51,
    D4 = 52,
    D5 = 53,
    D6 = 54,
    D7 = 55,
    D8 = 56,
    D9 = 57,
    Decimal = 110,
    Delete = 46,
    Divide = 111,
    Down = 40,
    E = 69,
    End = 35,
    Enter = 13,
    EraseEof = 249,
    Escape = 27,
    Execute = 43,
    Exsel = 248,
    F = 70,
    F1 = 112,
    F10 = 121,
    F11 = 122,
    F12 = 123,
    F13 = 124,
    F14 = 125,
    F15 = 126,
    F16 = 127,
    F17 = 128,
    F18 = 129,
    F19 = 130,
    F2 = 113,
    F20 = 131,
    F21 = 132,
    F22 = 133,
    F23 = 134,
    F24 = 135,
    F3 = 114,
    F4 = 115,
    F5 = 116,
    F6 = 117,
    F7 = 118,
    F8 = 119,
    F9 = 120,
    FinalMode = 24,
    G = 71,
    H = 72,
    HanguelMode = 21,
    HangulMode = 21,
    HanjaMode = 25,
    Help = 47,
    Home = 36,
    I = 73,
    IMEAccept = 30,
    IMEAceept = 30,
    IMEConvert = 28,
    IMEModeChange = 31,
    IMENonconvert = 29,
    Insert = 45,
    J = 74,
    JunjaMode = 23,
    K = 75,
    KanaMode = 21,
    KanjiMode = 25,
    KeyCode = 65535,
    L = 76,
    LaunchApplication1 = 182,
    LaunchApplication2 = 183,
    LaunchMail = 180,
    LButton = 1,
    LControlKey = 162,
    Left = 37,
    LineFeed = 10,
    LMenu = 164,
    LShiftKey = 160,
    LWin = 91,
    M = 77,
    MButton = 4,
    MediaNextTrack = 176,
    MediaPlayPause = 179,
    MediaPreviousTrack = 177,
    MediaStop = 178,
    Menu = 18,
    Modifiers = -65536,
    Multiply = 106,
    N = 78,
    Next = 34,
    NoName = 252,
    None = 0,
    NumLock = 144,
    NumPad0 = 96,
    NumPad1 = 97,
    NumPad2 = 98,
    NumPad3 = 99,
    NumPad4 = 100,
    NumPad5 = 101,
    NumPad6 = 102,
    NumPad7 = 103,
    NumPad8 = 104,
    NumPad9 = 105,
    O = 79,
    Oem1 = 186,
    Oem102 = 226,
    Oem2 = 191,
    Oem3 = 192,
    Oem4 = 219,
    Oem5 = 220,
    Oem6 = 221,
    Oem7 = 222,
    Oem8 = 223,
    OemBackslash = 226,
    OemClear = 254,
    OemCloseBrackets = 221,
    Oemcomma = 188,
    OemMinus = 189,
    OemOpenBrackets = 219,
    OemPeriod = 190,
    OemPipe = 220,
    Oemplus = 187,
    OemQuestion = 191,
    OemQuotes = 222,
    OemSemicolon = 186,
    Oemtilde = 192,
    P = 80,
    Pa1 = 253,
    Packet = 231,
    PageDown = 34,
    PageUp = 33,
    Pause = 19,
    Play = 250,
    Print = 42,
    PrintScreen = 44,
    Prior = 33,
    ProcessKey = 229,
    Q = 81,
    R = 82,
    RButton = 2,
    RControlKey = 163,
    Return = 13,
    Right = 39,
    RMenu = 165,
    RShiftKey = 161,
    RWin = 92,
    S = 83,
    Scroll = 145,
    Select = 41,
    SelectMedia = 181,
    Separator = 108,
    Shift = 65536,
    ShiftKey = 16,
    Sleep = 95,
    Snapshot = 44,
    Space = 32,
    Subtract = 109,
    T = 84,
    Tab = 9,
    U = 85,
    Up = 38,
    V = 86,
    VolumeDown = 174,
    VolumeMute = 173,
    VolumeUp = 175,
    W = 87,
    X = 88,
    XButton1 = 5,
    XButton2 = 6,
    Y = 89,
    Z = 90,
    Zoom = 251,
}

/// <summary>
/// Session display (matches System.Windows.Forms.Screen). The browser
/// exposes one viewport, so AllScreens is the single primary screen whose
/// Bounds/WorkingArea are the pinned SystemParameters virtual screen;
/// window-centering/clamping code (Editor, NewRecorder, dialogs) runs
/// unchanged against it. FromHandle/FromPoint/FromRectangle return that
/// screen: handles and multi-monitor coordinates do not exist here.
/// </summary>
public sealed class Screen
{
    private Screen()
    {
    }

    public static Screen[] AllScreens => [PrimaryScreen];

    public static Screen PrimaryScreen { get; } = new Screen();

    /// <summary>Session display name (matches the WinForms DISPLAY1 shape).</summary>
    public string DeviceName => "\\\\.\\DISPLAY1";

    public System.Drawing.Rectangle Bounds =>
        new(0, 0, (int)DesktopWasm.Wpf.SystemParameters.VirtualScreenWidth,
            (int)DesktopWasm.Wpf.SystemParameters.VirtualScreenHeight);

    /// <summary>Usable area (no taskbar in the browser, so it equals Bounds).</summary>
    public System.Drawing.Rectangle WorkingArea => Bounds;

    public int BitsPerPixel => 32;

    public bool Primary => true;

    /// <summary>Handles do not exist in the browser; answers the session screen.</summary>
    public static Screen FromHandle(IntPtr hwnd) => PrimaryScreen;

    /// <summary>Single-viewport hit test; answers the session screen.</summary>
    public static Screen FromPoint(System.Drawing.Point point) => PrimaryScreen;

    /// <summary>Single-viewport hit test; answers the session screen.</summary>
    public static Screen FromRectangle(System.Drawing.Rectangle rect) => PrimaryScreen;
}

/// <summary>
/// System metrics (matches System.Windows.Forms.SystemInformation). Values
/// converted code actually reads: wheel delta 120, remote-session false,
/// double-click from the live engine window, virtual screen from Screen.
/// </summary>
public static class SystemInformation
{
    public static int MouseWheelScrollDelta => 120;

    public static bool TerminalServerSession => false;

    /// <summary>Double-click window in ms (tracks the live engine value).</summary>
    public static int DoubleClickTime =>
        (int)DesktopWasm.Wpf.Button.DoubleClickWindow.TotalMilliseconds;

    public static System.Drawing.Rectangle VirtualScreen => Screen.PrimaryScreen.Bounds;
}

/// <summary>
/// UI-thread interval timer (matches System.Windows.Forms.Timer). Backed by
/// the engine DispatcherTimer, so Tick handlers run on the dispatcher queue
/// like the WinForms message-pump ticks the capture/highlight timers expect.
/// </summary>
public class Timer : Component
{
    private readonly DesktopWasm.Wpf.DispatcherTimer _inner = new();

    public Timer()
    {
        _inner.Interval = TimeSpan.FromMilliseconds(100);
        _inner.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Tick;

    /// <summary>Interval in ms (default 100; zero/negative throws like WinForms).</summary>
    public int Interval
    {
        get => (int)_inner.Interval.TotalMilliseconds;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Interval must be greater than zero.");
            _inner.Interval = TimeSpan.FromMilliseconds(value);
        }
    }

    public bool Enabled
    {
        get => _inner.IsEnabled;
        set => _inner.IsEnabled = value;
    }

    public void Start() => _inner.Start();

    public void Stop() => _inner.Stop();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Stop();
        base.Dispose(disposing);
    }
}

} namespace System.Security.AccessControl {

/// <summary>
/// Mutex rights (matches System.Security.AccessControl.MutexRights; values
/// verified against the net10 reference assembly).
/// </summary>
[Flags]
public enum MutexRights
{
    Modify = 1,
    Delete = 65536,
    ReadPermissions = 131072,
    ChangePermissions = 262144,
    TakeOwnership = 524288,
    Synchronize = 1048576,
    FullControl = 2031617,
}

/// <summary>
/// Mutex access entry (matches System.Security.AccessControl.MutexAccessRule:
/// real AccessRule base with a real NTAccount identity, real access mask).
/// Util builds these for the single-instance/project mutexes.
/// </summary>
public sealed class MutexAccessRule : AccessRule
{
    public MutexAccessRule(IdentityReference identity, MutexRights rights, AccessControlType type)
        : base(identity, (int)rights, false, InheritanceFlags.None, PropagationFlags.None, type)
    {
    }

    public MutexAccessRule(string name, MutexRights rights, AccessControlType type)
        : this(new NTAccount(name), rights, type)
    {
    }

    public MutexRights MutexRights => (MutexRights)AccessMask;
}

/// <summary>
/// Mutex security descriptor (covers System.Security.AccessControl.MutexSecurity).
/// A retained rule store: Add/Remove/Get round-trip exactly; there is no OS
/// enforcement behind it (named-mutex ACLs do not exist in the browser, so
/// Util's world-access single-instance rule is accepted and retained while
/// the single-instance scope stays the session). Deliberately standalone
/// instead of NativeObjectSecurity: persisting an ACL to a nonexistent
/// securable would be the fabrication.
/// </summary>
public sealed class MutexSecurity
{
    private readonly List<MutexAccessRule> _rules = new();

    public void AddAccessRule(MutexAccessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
    }

    public bool RemoveAccessRule(MutexAccessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return _rules.Remove(rule);
    }

    public AuthorizationRuleCollection GetAccessRules(bool includeExplicit, bool includeInherited, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        var result = new AuthorizationRuleCollection();
        foreach (var rule in _rules)
        {
            if (!includeExplicit) continue;
            if (targetType.IsInstanceOfType(rule)) result.AddRule(rule);
        }
        return result;
    }
}

} namespace System.Threading {

/// <summary>
/// Mutex ACL association (matches System.Threading.ThreadingAclExtensions).
/// The accepted descriptor is retained per Mutex instance, so
/// GetAccessControl round-trips what SetAccessControl stored.
/// </summary>
public static class ThreadingAclExtensions
{
    private static readonly ConditionalWeakTable<Mutex, System.Security.AccessControl.MutexSecurity> Store = new();

    public static void SetAccessControl(this Mutex mutex, System.Security.AccessControl.MutexSecurity security)
    {
        ArgumentNullException.ThrowIfNull(mutex);
        ArgumentNullException.ThrowIfNull(security);
        Store.AddOrUpdate(mutex, security);
    }

    public static System.Security.AccessControl.MutexSecurity GetAccessControl(this Mutex mutex)
    {
        ArgumentNullException.ThrowIfNull(mutex);
        return Store.GetValue(mutex, _ => new System.Security.AccessControl.MutexSecurity());
    }
}

} namespace System.Security.Cryptography {

/// <summary>DPAPI scope (matches System.Security.Cryptography.DataProtectionScope).</summary>
public enum DataProtectionScope
{
    CurrentUser = 0,
    LocalMachine = 1,
}

/// <summary>
/// Data protection (covers System.Security.Cryptography.ProtectedData).
/// AES-256-CBC + HMAC-SHA256 under per-process random keys with the scope
/// and entropy folded into the authentication tag, so Protect/Unprotect
/// round-trip inside the session, scope or entropy mismatches fail, and
/// tampered blobs fail instead of decrypting. Util's WebHelper secret store
/// runs unchanged on this. Unlike DPAPI user scope the ciphertext does not
/// survive restart: keys are deliberately never persisted (there is no
/// browser key store), which the tests pin.
/// </summary>
public static class ProtectedData
{
    private const byte Version = 1;
    private static readonly byte[] EncKey = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] MacKey = RandomNumberGenerator.GetBytes(32);

    public static byte[] Protect(byte[] userData, byte[]? optionalEntropy, DataProtectionScope scope)
    {
        ArgumentNullException.ThrowIfNull(userData);
        using var aes = Aes.Create();
        aes.Key = EncKey;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(userData, 0, userData.Length);
        var entropy = optionalEntropy ?? [];
        var payload = new byte[1 + aes.IV.Length + cipher.Length];
        payload[0] = Version;
        Buffer.BlockCopy(aes.IV, 0, payload, 1, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, payload, 1 + aes.IV.Length, cipher.Length);
        var tag = Tag(payload, scope, entropy);
        var result = new byte[payload.Length + tag.Length];
        Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
        Buffer.BlockCopy(tag, 0, result, payload.Length, tag.Length);
        return result;
    }

    public static byte[] Unprotect(byte[] encryptedData, byte[]? optionalEntropy, DataProtectionScope scope)
    {
        ArgumentNullException.ThrowIfNull(encryptedData);
        var entropy = optionalEntropy ?? [];
        const int ivLength = 16;
        const int tagLength = 32;
        if (encryptedData.Length < 1 + ivLength + tagLength || encryptedData[0] != Version)
            throw new CryptographicException("The parameter is incorrect.");
        var payloadLength = encryptedData.Length - tagLength;
        var payload = encryptedData.AsSpan(0, payloadLength);
        var expected = Tag(payload.ToArray(), scope, entropy);
        if (!CryptographicOperations.FixedTimeEquals(encryptedData.AsSpan(payloadLength), expected))
            throw new CryptographicException("The parameter is incorrect.");
        var iv = payload.Slice(1, ivLength).ToArray();
        var cipher = payload.Slice(1 + ivLength).ToArray();
        using var aes = Aes.Create();
        aes.Key = EncKey;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    private static byte[] Tag(byte[] payload, DataProtectionScope scope, byte[] entropy)
    {
        using var hmac = new HMACSHA256(MacKey);
        hmac.TransformBlock([(byte)scope], 0, 1, null, 0);
        if (entropy.Length > 0) hmac.TransformBlock(entropy, 0, entropy.Length, null, 0);
        hmac.TransformFinalBlock(payload, 0, payload.Length);
        return hmac.Hash!;
    }
}

} namespace System.CodeDom {

/// <summary>
/// Code type declaration (return-slot mirror for
/// System.CodeDom.CodeTypeDeclaration: ManagementClass.GetStronglyTypedClassCode
/// carries it as its converted return type). Real ctor shapes plus
/// TypeAttributes-backed kind flags with the documented CodeDom semantics;
/// member/codegen graphs are absent because the WMI strong-type codegen path
/// never executes in the browser (WMI queries throw first; Feedback renders
/// its own designed fallback rows).
/// </summary>
public class CodeTypeDeclaration
{
    private enum Kind
    {
        Class,
        Struct,
        Enum,
        Interface,
    }

    private Kind _kind = Kind.Class;
    private System.Reflection.TypeAttributes _attributes = System.Reflection.TypeAttributes.Public;

    public CodeTypeDeclaration()
    {
    }

    public CodeTypeDeclaration(string? name) => Name = name;

    public string? Name { get; set; }

    public System.Reflection.TypeAttributes TypeAttributes
    {
        get => _attributes;
        set
        {
            _attributes = value;
            if ((value & System.Reflection.TypeAttributes.ClassSemanticsMask) ==
                System.Reflection.TypeAttributes.Interface)
                _kind = Kind.Interface;
            else if (_kind == Kind.Interface)
                _kind = Kind.Class;
        }
    }

    /// <summary>Setting true returns to class (and clears the interface bit); false is a no-op.</summary>
    public bool IsClass
    {
        get => _kind == Kind.Class;
        set
        {
            if (!value) return;
            _kind = Kind.Class;
            _attributes &= ~System.Reflection.TypeAttributes.ClassSemanticsMask;
        }
    }

    public bool IsInterface
    {
        get => _kind == Kind.Interface;
        set
        {
            if (value)
            {
                _kind = Kind.Interface;
                _attributes = (_attributes & ~System.Reflection.TypeAttributes.ClassSemanticsMask) |
                    System.Reflection.TypeAttributes.Interface;
            }
            else
            {
                _kind = Kind.Class;
                _attributes &= ~System.Reflection.TypeAttributes.ClassSemanticsMask;
            }
        }
    }

    public bool IsStruct
    {
        get => _kind == Kind.Struct;
        set => _kind = value ? Kind.Struct : Kind.Class;
    }

    public bool IsEnum
    {
        get => _kind == Kind.Enum;
        set => _kind = value ? Kind.Enum : Kind.Class;
    }

    public bool IsPartial { get; set; }
}
}

// Slice-48 OxyPlot view-assembly prerequisites (shim, not port).
//
// OxyPlot.Wpf converts against real WPF only once these four absent
// upstream assemblies map onto engine counterparts: System.IO.Packaging
// (XpsExporter packaging), ReachFramework (XpsDocument), System.Printing
// (PrintQueue/PrintDocumentImageableArea/XpsDocumentWriter) and
// System.Configuration.ConfigurationManager (generated Properties.Settings).
// Verified against live .NET 10 on 2026-10-26 (PresentationFramework for
// SerializerWriter/TextOptions/ValueConversionAttribute, PresentationCore
// for ClearTypeHint/manipulation, ReachFramework for XpsDocument,
// System.Printing for XpsDocumentWriter/PrintQueue/PrintDocumentImageableArea,
// System.IO.Packaging for Package, System.Configuration.ConfigurationManager
// for SettingsBase/ApplicationSettingsBase): member scope below is exactly
// what converted OxyPlot IL names (Package.Open(Stream,FileMode,FileAccess);
// XpsDocument(Package)/CreateXpsDocumentWriter(XpsDocument); XpsDocumentWriter
// inherited Write(Visual); PrintQueue.CreateXpsDocumentWriter(ref area);
// PrintDocumentImageableArea ExtentWidth/ExtentHeight; SettingsBase.Synchronized;
// ApplicationSettingsBase ()).
//
// Retained limits: XPS/print/settings payloads never ship to the browser.
// Construction is retained (converted export/print/settings paths JIT and
// run until the device call), while every device/packaging call is a precise
// NotSupportedException boundary — the same contract as the EventLog slice
// (construction retained, Windows-only payload never staged).

namespace System.IO.Packaging {

/// <summary>
/// Packaging root (matches System.IO.Packaging.Package, referenced subset:
/// abstract, IDisposable, static Open(Stream, FileMode, FileAccess), Close).
/// Converted OxyPlot XpsExporter.Export packages into a stream; the browser
/// has no OPC packaging device, so Open is a precise boundary.
/// </summary>
public abstract class Package : IDisposable
{
    public static Package Open(System.IO.Stream stream, System.IO.FileMode packageMode, System.IO.FileAccess packageAccess)
    {
        ArgumentNullException.ThrowIfNull(stream);
        throw new NotSupportedException("XPS packaging is not available in the browser engine.");
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}

} namespace System.Windows.Xps.Packaging {

/// <summary>
/// XPS document (matches System.Windows.Xps.Packaging.XpsDocument, referenced
/// subset: public ctors over Package, static CreateXpsDocumentWriter, Close,
/// IDisposable). Construction is retained so converted export paths JIT;
/// writing/printing through the document hits the SerializerWriter boundary.
/// </summary>
public class XpsDocument : IDisposable
{
    public XpsDocument(System.IO.Packaging.Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Package = package;
    }

    public System.IO.Packaging.Package Package { get; }

    public static System.Windows.Xps.XpsDocumentWriter CreateXpsDocumentWriter(XpsDocument xpsDocument)
    {
        ArgumentNullException.ThrowIfNull(xpsDocument);
        return new System.Windows.Xps.XpsDocumentWriter();
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}

} namespace System.Windows.Xps {

/// <summary>
/// XPS document writer (matches System.Windows.Xps.XpsDocumentWriter:
/// SerializerWriter subclass). Converted call sites bind the inherited
/// Write(Visual) boundary; no writer state exists in the browser.
/// </summary>
public class XpsDocumentWriter : DesktopWasm.Wpf.SerializerWriter
{
    internal XpsDocumentWriter()
    {
    }
}

} namespace System.Printing {

/// <summary>
/// Print queue (matches System.Printing.PrintQueue, referenced subset:
/// static CreateXpsDocumentWriter(ref PrintDocumentImageableArea)).
/// Converted OxyPlot XpsExporter.Print queries the spooler; the browser has
/// no print spooler, so the call is a precise boundary.
/// </summary>
public class PrintQueue
{
    public static System.Windows.Xps.XpsDocumentWriter CreateXpsDocumentWriter(ref PrintDocumentImageableArea documentImageableArea)
    {
        documentImageableArea = new PrintDocumentImageableArea();
        throw new NotSupportedException("Printing is not available in the browser engine.");
    }
}

/// <summary>
/// Printable-area data (matches
/// System.Printing.PrintDocumentImageableArea, read subset: ExtentWidth/
/// ExtentHeight plus the sibling area metrics). Pure data with no device
/// behind it, so every metric reads 0 in the browser.
/// </summary>
public class PrintDocumentImageableArea
{
    public PrintDocumentImageableArea()
    {
    }

    public double MediaSizeHeight { get; }
    public double MediaSizeWidth { get; }
    public double ExtentHeight { get; }
    public double ExtentWidth { get; }
    public double OriginHeight { get; }
    public double OriginWidth { get; }
}

} namespace System.Configuration {

/// <summary>
/// Settings root (matches System.Configuration.SettingsBase, referenced
/// subset: static Synchronized). Converted designer Settings call
/// Synchronized in their static constructor; the browser has no roaming
/// settings sync, so the wrapper returns the instance unchanged.
/// </summary>
public abstract class SettingsBase
{
    protected SettingsBase()
    {
    }

    public static SettingsBase Synchronized(SettingsBase settingsBase)
    {
        ArgumentNullException.ThrowIfNull(settingsBase);
        return settingsBase;
    }
}

/// <summary>
/// Application settings root (matches
/// System.Configuration.ApplicationSettingsBase, referenced subset: the
/// protected parameterless ctor converted designer Settings derive).
/// Property/event payloads (providers, roaming values) do not exist in the
/// browser session.
/// </summary>
public abstract class ApplicationSettingsBase : SettingsBase
{
    protected ApplicationSettingsBase()
    {
    }
}

/// <summary>
/// Scoped app-settings facade (matches
/// System.Configuration.ConfigurationManager, referenced subset:
/// AppSettings only). Converted demos read static links/keys through it
/// (e.g. documentation URLs). Values seed from the converted image's
/// "__AppSettings.json" resource, which the packager writes from the real
/// upstream ".dll.config" &lt;appSettings&gt; block; without that resource
/// every key reads null, exactly like a missing config section on desktop.
/// </summary>
public static class ConfigurationManager
{
    private static System.Collections.Specialized.NameValueCollection? _appSettings;

    public static System.Collections.Specialized.NameValueCollection AppSettings =>
        _appSettings ??= new System.Collections.Specialized.NameValueCollection();

    internal static void SeedAppSettings(IEnumerable<KeyValuePair<string, string?>> values)
    {
        var table = new System.Collections.Specialized.NameValueCollection();
        foreach (var (key, value) in values)
            table.Add(key, value);
        _appSettings = table;
    }

    internal static void ResetAppSettingsForTests() => _appSettings = null;
}
}

namespace DesktopWasm.Wpf {

/// <summary>
/// XPS document-writer root (matches
/// System.Windows.Documents.Serialization.SerializerWriter: abstract, virtual
/// Write(Visual)). Converted OxyPlot XpsExporter.Write calls Write(Visual)
/// through this base; the browser has no XPS serializer, so the call is a
/// precise boundary while the type/override graph converts. Lives in the flat
/// engine namespace like every other System.Windows.* default-rule type.
/// </summary>
public abstract class SerializerWriter
{
    protected SerializerWriter()
    {
    }

    public virtual void Write(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        throw new NotSupportedException("XPS serialization is not available in the browser engine.");
    }
}
}
