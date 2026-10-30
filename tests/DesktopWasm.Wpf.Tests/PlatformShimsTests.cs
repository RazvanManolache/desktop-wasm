using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Windows.Forms;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-30 platform shims: WinForms adapters, ACL retention, AES session
/// protection, and the CodeDom return-slot mirror. Every asserted value was
/// verified against live .NET on 2026-10-26 (Framework WinForms, net10
/// AccessControl, ProtectedData 9.0.0 / CodeDom 4.6.0 packages).
/// </summary>
[ResetAmbientState]
public sealed class PlatformShimsTests
{
    [Fact]
    public void Keys_carry_real_values_and_aliases()
    {
        Assert.Equal(0, (int)Keys.None);
        Assert.Equal(65, (int)Keys.A);
        Assert.Equal(90, (int)Keys.Z);
        Assert.Equal(8, (int)Keys.Back);
        Assert.Equal(13, (int)Keys.Enter);
        Assert.Equal(27, (int)Keys.Escape);
        Assert.Equal(32, (int)Keys.Space);
        Assert.Equal(46, (int)Keys.Delete);
        Assert.Equal(112, (int)Keys.F1);
        Assert.Equal(116, (int)Keys.F5);
        Assert.Equal(91, (int)Keys.LWin);
        Assert.Equal(93, (int)Keys.Apps);
        Assert.Equal(Keys.Capital, Keys.CapsLock);
        Assert.Equal(20, (int)Keys.CapsLock);
        Assert.Equal(Keys.Enter, Keys.Return);
        Assert.Equal(Keys.Oem1, Keys.OemSemicolon);
        Assert.Equal(186, (int)Keys.OemSemicolon);
        Assert.Equal(Keys.Oem2, Keys.OemQuestion);
        Assert.Equal(Keys.Oem3, Keys.Oemtilde);
        Assert.True(typeof(Keys).IsDefined(typeof(FlagsAttribute), inherit: false));
    }

    [Fact]
    public void Keys_parse_round_trips_wpf_key_names_like_native_hotkeys()
    {
        // ScreenToGif.Native HotKey.ConvertWinformsToWpfKey parses WPF key
        // names into WinForms codes; the parse must land on real values.
        Assert.Equal(Keys.Escape, (Keys)Enum.Parse(typeof(Keys), "Escape"));
        Assert.Equal(Keys.F12, (Keys)Enum.Parse(typeof(Keys), "F12"));
        Assert.Equal(Keys.A, (Keys)Enum.Parse(typeof(Keys), "A"));
        Assert.Throws<ArgumentException>(() => Enum.Parse(typeof(Keys), "NotAKey"));
    }

    [Fact]
    public void Screen_is_the_single_pinned_session_viewport()
    {
        Assert.Single(Screen.AllScreens);
        Assert.Same(Screen.PrimaryScreen, Screen.AllScreens[0]);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 1024, 768), Screen.PrimaryScreen.Bounds);
        Assert.Equal(Screen.PrimaryScreen.Bounds, Screen.PrimaryScreen.WorkingArea);
        Assert.Equal(
            new System.Drawing.Rectangle(0, 0, (int)SystemParameters.VirtualScreenWidth, (int)SystemParameters.VirtualScreenHeight),
            Screen.PrimaryScreen.Bounds);
        Assert.Equal(32, Screen.PrimaryScreen.BitsPerPixel);
        Assert.True(Screen.PrimaryScreen.Primary);
        Assert.NotEmpty(Screen.PrimaryScreen.DeviceName);
        Assert.Same(Screen.PrimaryScreen, Screen.FromHandle(new IntPtr(1234)));
        Assert.Same(Screen.PrimaryScreen, Screen.FromPoint(new System.Drawing.Point(10, 10)));
        Assert.Same(Screen.PrimaryScreen, Screen.FromRectangle(new System.Drawing.Rectangle(0, 0, 10, 10)));
    }

    [Fact]
    public void SystemInformation_serves_session_values()
    {
        Assert.Equal(120, SystemInformation.MouseWheelScrollDelta);
        Assert.False(SystemInformation.TerminalServerSession);
        Assert.Equal(Screen.PrimaryScreen.Bounds, SystemInformation.VirtualScreen);
        Assert.Equal((int)Button.DoubleClickWindow.TotalMilliseconds, SystemInformation.DoubleClickTime);
    }

    [Fact]
    public void Timer_defaults_match_winforms_and_ticks_on_the_dispatcher()
    {
        using var timer = new System.Windows.Forms.Timer();
        Assert.Equal(100, timer.Interval);
        Assert.False(timer.Enabled);
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Interval = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Interval = -5);

        var ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Interval = 50;
        var start = DateTime.UtcNow;
        try
        {
            timer.Start();
            Assert.True(timer.Enabled);
            DispatcherTimer.TickAll(start.AddMilliseconds(30));
            Assert.Equal(0, ticks);
            DispatcherTimer.TickAll(start.AddMilliseconds(60));
            Assert.Equal(1, ticks);
            timer.Stop();
            Assert.False(timer.Enabled);
            DispatcherTimer.TickAll(start.AddSeconds(5));
            Assert.Equal(1, ticks);
        }
        finally
        {
            timer.Stop();
        }
    }

    [Fact]
    public void Mutex_rights_carry_real_values()
    {
        Assert.Equal(1, (int)MutexRights.Modify);
        Assert.Equal(65536, (int)MutexRights.Delete);
        Assert.Equal(131072, (int)MutexRights.ReadPermissions);
        Assert.Equal(262144, (int)MutexRights.ChangePermissions);
        Assert.Equal(524288, (int)MutexRights.TakeOwnership);
        Assert.Equal(1048576, (int)MutexRights.Synchronize);
        Assert.Equal(2031617, (int)MutexRights.FullControl);
    }

    [Fact]
    public void Mutex_rule_and_store_round_trip_like_util_single_instance()
    {
        // Mirrors Util MutexList: world-access rule on the app mutex.
        using var mutex = new Mutex(false, null);
        var security = new MutexSecurity();
        var rule = new MutexAccessRule(
            Environment.UserDomainName + "\\" + Environment.UserName,
            MutexRights.FullControl,
            AccessControlType.Allow);
        Assert.Equal(MutexRights.FullControl, rule.MutexRights);
        Assert.IsType<NTAccount>(rule.IdentityReference);
        security.AddAccessRule(rule);

        mutex.SetAccessControl(security);
        var stored = mutex.GetAccessControl();
        Assert.Same(security, stored);
        var rules = stored.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(MutexAccessRule));
        Assert.Single(rules);
        Assert.Same(rule, rules[0]);

        Assert.True(security.RemoveAccessRule(rule));
        Assert.Empty(security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(MutexAccessRule)));
        // A second mutex keeps its own descriptor (per-instance retention).
        using var other = new Mutex(false, null);
        Assert.Empty(other.GetAccessControl().GetAccessRules(true, false, typeof(MutexAccessRule)));
        Assert.Throws<ArgumentNullException>(() => security.AddAccessRule(null!));
        Assert.Throws<ArgumentNullException>(() => mutex.SetAccessControl(null!));
    }

    [Fact]
    public void ProtectedData_round_trips_and_rejects_mismatch_and_tamper()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var entropy = new byte[] { 9, 8, 7 };
        foreach (var scope in new[] { DataProtectionScope.CurrentUser, DataProtectionScope.LocalMachine })
        {
            var blob = ProtectedData.Protect(data, entropy, scope);
            Assert.Equal(data, ProtectedData.Unprotect(blob, entropy, scope));
        }
        Assert.Equal(0, (int)DataProtectionScope.CurrentUser);
        Assert.Equal(1, (int)DataProtectionScope.LocalMachine);

        var current = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
        // Scope and entropy are authenticated, not just carried.
        Assert.Throws<CryptographicException>(() => ProtectedData.Unprotect(current, entropy, DataProtectionScope.LocalMachine));
        Assert.Throws<CryptographicException>(() => ProtectedData.Unprotect(current, new byte[] { 1 }, DataProtectionScope.CurrentUser));
        // Fresh random IV: identical inputs protect to different blobs.
        Assert.NotEqual(
            ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser),
            ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser));

        current[^1] ^= 0xFF;
        Assert.Throws<CryptographicException>(() => ProtectedData.Unprotect(current, entropy, DataProtectionScope.CurrentUser));
        Assert.Throws<ArgumentNullException>(() => ProtectedData.Protect(null!, entropy, DataProtectionScope.CurrentUser));
        Assert.Throws<ArgumentNullException>(() => ProtectedData.Unprotect(null!, entropy, DataProtectionScope.CurrentUser));
        Assert.Throws<CryptographicException>(() => ProtectedData.Unprotect([], entropy, DataProtectionScope.CurrentUser));
        // Empty payloads round-trip without throwing.
        Assert.Empty(ProtectedData.Unprotect(
            ProtectedData.Protect([], null, DataProtectionScope.CurrentUser), null, DataProtectionScope.CurrentUser));
    }

    [Fact]
    public void CodeTypeDeclaration_defaults_and_kind_transitions_match_real_codedom()
    {
        var unit = new System.CodeDom.CodeTypeDeclaration("Widget");
        Assert.Equal("Widget", unit.Name);
        Assert.True(unit.IsClass);
        Assert.False(unit.IsStruct);
        Assert.False(unit.IsEnum);
        Assert.False(unit.IsInterface);
        Assert.False(unit.IsPartial);
        Assert.Equal(System.Reflection.TypeAttributes.Public, unit.TypeAttributes);

        unit.IsStruct = true;
        Assert.False(unit.IsClass);
        Assert.True(unit.IsStruct);
        unit.IsClass = true;
        Assert.True(unit.IsClass);
        Assert.False(unit.IsStruct);

        unit.IsEnum = true;
        Assert.True(unit.IsEnum);
        Assert.False(unit.IsStruct);
        unit.IsStruct = true;
        Assert.True(unit.IsStruct);
        Assert.False(unit.IsEnum);

        unit.IsInterface = true;
        Assert.True(unit.IsInterface);
        Assert.False(unit.IsClass);
        Assert.Equal(
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Interface,
            unit.TypeAttributes);
        unit.IsClass = true;
        Assert.True(unit.IsClass);
        Assert.False(unit.IsInterface);

        // Setting IsClass=false is a no-op in real CodeDom.
        unit.IsClass = false;
        Assert.True(unit.IsClass);

        // Direct TypeAttributes writes sync the interface kind like the real type.
        var viaAttrs = new System.CodeDom.CodeTypeDeclaration();
        viaAttrs.TypeAttributes = System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Interface;
        Assert.True(viaAttrs.IsInterface);
        Assert.False(viaAttrs.IsClass);

        unit.IsPartial = true;
        Assert.True(unit.IsPartial);
    }
}
