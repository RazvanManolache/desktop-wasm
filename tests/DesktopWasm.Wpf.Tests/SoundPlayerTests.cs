using System.ComponentModel;
using System.Media;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-76 HandyControl entry audio scope: SoundPlayer defaults,
/// source/change events, real WAVE header validation on Load, retained
/// play/stop state over the documented no-audio-sink boundary, and the
/// exact entry call shape (new SoundPlayer(path) + PlaySync). Member
/// evidence via Mono.Cecil on the upstream net10.0-windows
/// HandyControlDemo.dll (ChatBoxViewModel.ReadMessage is the only
/// System.Windows.Extensions reference in the entry); shapes live-verified
/// against the 9.0.21 System.Windows.Extensions ref pack (Component base,
/// ()/(Stream)/(string) ctors, IsLoadCompleted/LoadTimeout/SoundLocation/
/// Stream/Tag, LoadCompleted/SoundLocationChanged/StreamChanged,
/// Load/LoadAsync/On*/Play/PlayLooping/PlaySync/Stop).</summary>
[ResetAmbientState]
public sealed class SoundPlayerTests
{
    private static byte[] WavBytes(int channels = 1, int rate = 8000, int bits = 16, int samples = 8)
    {
        var block = channels * bits / 8;
        var data = new byte[samples * block];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + data.Length);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * block);
        writer.Write((short)block);
        writer.Write((short)bits);
        writer.Write("data".ToCharArray());
        writer.Write(data.Length);
        writer.Write(data);
        return stream.ToArray();
    }

    [Fact]
    public void Defaults_match_wpf()
    {
        using var player = new SoundPlayer();
        Assert.Equal(string.Empty, player.SoundLocation);
        Assert.Null(player.Stream);
        Assert.Null(player.Tag);
        Assert.Equal(10000, player.LoadTimeout);
        Assert.False(player.IsLoadCompleted);
        Assert.False(player.IsPlaying);
        Assert.False(player.IsLooping);
        Assert.Null(player.LoadedFormat);
    }

    [Fact]
    public void SoundPlayer_matches_wpf_shape()
    {
        Assert.True(typeof(SoundPlayer).IsSubclassOf(typeof(Component)));
        Assert.False(typeof(SoundPlayer).IsSealed);
        Assert.NotNull(typeof(SoundPlayer).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(SoundPlayer).GetConstructor([typeof(string)]));
        Assert.NotNull(typeof(SoundPlayer).GetConstructor([typeof(Stream)]));
        Assert.NotNull(typeof(SoundPlayer).GetProperty("IsLoadCompleted"));
        Assert.NotNull(typeof(SoundPlayer).GetProperty("LoadTimeout"));
        Assert.NotNull(typeof(SoundPlayer).GetProperty("SoundLocation"));
        Assert.NotNull(typeof(SoundPlayer).GetProperty("Stream"));
        Assert.NotNull(typeof(SoundPlayer).GetProperty("Tag"));
        Assert.NotNull(typeof(SoundPlayer).GetEvent("LoadCompleted"));
        Assert.NotNull(typeof(SoundPlayer).GetEvent("SoundLocationChanged"));
        Assert.NotNull(typeof(SoundPlayer).GetEvent("StreamChanged"));
        foreach (var name in new[] { "Load", "LoadAsync", "Play", "PlayLooping", "PlaySync", "Stop" })
            Assert.NotNull(typeof(SoundPlayer).GetMethod(name, Type.EmptyTypes));
        Assert.Equal(typeof(AsyncCompletedEventHandler), typeof(SoundPlayer).GetEvent("LoadCompleted")!.EventHandlerType);
    }

    [Fact]
    public void Location_ctor_lands_and_negative_timeout_throws()
    {
        using var player = new SoundPlayer("chat.wav");
        Assert.Equal("chat.wav", player.SoundLocation);
        Assert.Throws<ArgumentOutOfRangeException>(() => player.LoadTimeout = -1);
    }

    [Fact]
    public void Source_changes_raise_once_and_reset_load()
    {
        using var player = new SoundPlayer();
        using var wave = new MemoryStream(WavBytes());
        player.Stream = wave;
        player.Load();
        Assert.True(player.IsLoadCompleted);
        var locationEvents = 0;
        var streamEvents = 0;
        player.SoundLocationChanged += (_, _) => locationEvents++;
        player.StreamChanged += (_, _) => streamEvents++;
        player.SoundLocation = "next.wav";
        Assert.Equal(1, locationEvents);
        Assert.False(player.IsLoadCompleted);
        Assert.Null(player.LoadedFormat);
        player.SoundLocation = "next.wav";
        Assert.Equal(1, locationEvents);
        using var other = new MemoryStream(WavBytes());
        player.Stream = other;
        Assert.Equal(1, streamEvents);
        player.Stream = other;
        Assert.Equal(1, streamEvents);
    }

    [Fact]
    public void Load_parses_real_wave_header()
    {
        using var player = new SoundPlayer(new MemoryStream(WavBytes(channels: 2, rate: 44100, bits: 16)));
        player.Load();
        Assert.True(player.IsLoadCompleted);
        Assert.Equal(new WavFormat(2, 44100, 16, 32), player.LoadedFormat);
    }

    [Fact]
    public void LoadAsync_loads_then_raises_completion()
    {
        using var player = new SoundPlayer(new MemoryStream(WavBytes()));
        AsyncCompletedEventArgs? raised = null;
        player.LoadCompleted += (_, e) => raised = e;
        player.LoadAsync();
        Assert.True(player.IsLoadCompleted);
        Assert.NotNull(raised);
        Assert.Null(raised!.Error);
        Assert.False(raised.Cancelled);
    }

    [Fact]
    public void Load_rejects_non_wave_loudly()
    {
        using var garbage = new SoundPlayer(new MemoryStream([0x49, 0x44, 0x33, 0x04, 0x00, 0x00]));
        Assert.Throws<InvalidDataException>(() => garbage.Load());
        Assert.False(garbage.IsLoadCompleted);
        using var truncated = new SoundPlayer(new MemoryStream([0x52, 0x49, 0x46, 0x46]));
        Assert.Throws<InvalidDataException>(() => truncated.Load());
        using var empty = new SoundPlayer();
        Assert.Throws<InvalidOperationException>(() => empty.Load());
        Assert.Throws<InvalidOperationException>(() => empty.PlaySync());
    }

    [Fact]
    public void Load_missing_file_throws_file_not_found()
    {
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wav");
        using var player = new SoundPlayer(missing);
        Assert.Throws<FileNotFoundException>(() => player.Load());
        Assert.False(player.IsLoadCompleted);
    }

    [Fact]
    public void Play_paths_retain_state_and_stop_clears()
    {
        using var player = new SoundPlayer(new MemoryStream(WavBytes()));
        player.Play();
        Assert.True(player.IsLoadCompleted);
        Assert.True(player.IsPlaying);
        Assert.False(player.IsLooping);
        player.PlayLooping();
        Assert.True(player.IsPlaying);
        Assert.True(player.IsLooping);
        player.Stop();
        Assert.False(player.IsPlaying);
        Assert.False(player.IsLooping);
        player.PlaySync();
        Assert.False(player.IsPlaying);
        player.Dispose();
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void Entry_call_shape_plays_a_real_wav_file()
    {
        // Mirrors HandyControlDemo ChatBoxViewModel.ReadMessage:
        // new SoundPlayer(path) + PlaySync() over a real WAVE file.
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(path, WavBytes());
            using var player = new SoundPlayer(path);
            player.PlaySync();
            Assert.True(player.IsLoadCompleted);
            Assert.Equal(new WavFormat(1, 8000, 16, 16), player.LoadedFormat);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
