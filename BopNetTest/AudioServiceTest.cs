using System.Diagnostics;
using System.Reflection;
using BopNet.Models;
using BopNet.Services.AudioService;
using BopNet.Services.TrackCacheService;

namespace BopNetTest;

public class AudioServiceTest
{
    private const ulong GuildId = 42;
    private AudioService _service = null!;
    private Dictionary<ulong, GuildAudio> _sessions = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new AudioService(new UnusedTrackCache());
        // Seed playback without downloading audio or requiring FFmpeg.
        _sessions = (Dictionary<ulong, GuildAudio>)typeof(AudioService)
            .GetField("_ffmpegProcesses", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_service)!;
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(137)]
    [TestCase(GuildAudio.BufferSize)]
    [TestCase(GuildAudio.BufferSize + 137)]
    public async Task StreamToDiscordAsync_WritesOnlySourceBytes(int length)
    {
        using var source = StartSource();
        var expected = Enumerable.Range(0, length).Select(i => (byte)(i % 251 + 1)).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var destination = new MemoryStream();
        _sessions.Add(GuildId, new GuildAudio { Ffmpeg = source });

        var streaming = _service.StreamToDiscordAsync(destination, GuildId, timeout.Token);
        await source.StandardInput.BaseStream.WriteAsync(expected, timeout.Token);
        source.StandardInput.Close();
        await streaming;
        await source.WaitForExitAsync(timeout.Token);

        Assert.That(destination.ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public async Task StreamToDiscordAsync_WhenPaused_WritesOneSilentFrameUntilCancelled()
    {
        using var source = StartSource();
        source.StandardInput.Close();
        using var cancellation = new CancellationTokenSource();
        using var destination = new CancelAfterWriteStream(cancellation);
        _sessions.Add(GuildId, new GuildAudio { Ffmpeg = source, Paused = true });

        await _service.StreamToDiscordAsync(destination, GuildId, cancellation.Token);

        Assert.That(destination.ToArray(), Is.EqualTo(new byte[GuildAudio.BufferSize]));
    }

    [Test]
    public async Task StreamToDiscordAsync_WhenAlreadyCancelled_WritesNothing()
    {
        using var source = StartSource();
        source.StandardInput.Close();
        using var destination = new MemoryStream();
        _sessions.Add(GuildId, new GuildAudio { Ffmpeg = source });

        await _service.StreamToDiscordAsync(destination, GuildId, new CancellationToken(true));

        Assert.That(destination.Length, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StreamToDiscordAsync_WithoutAnAudioProcess_WritesNothing(bool hasSession)
    {
        if (hasSession) _sessions.Add(GuildId, new GuildAudio());
        using var destination = new MemoryStream();

        await _service.StreamToDiscordAsync(destination, GuildId, CancellationToken.None);

        Assert.That(destination.Length, Is.Zero);
    }

    [Test]
    public void PauseAndResume_OnlyChangeTheRequestedGuild()
    {
        var audio = new GuildAudio();
        var otherAudio = new GuildAudio();
        _sessions.Add(GuildId, audio);
        _sessions.Add(GuildId + 1, otherAudio);

        _service.PauseAudio(GuildId);
        Assert.That(audio.Paused, Is.True);
        Assert.That(otherAudio.Paused, Is.False);

        _service.ResumeAudio(GuildId);
        Assert.That(audio.Paused, Is.False);
    }

    [Test]
    public void PlaybackControls_ForUnknownGuild_DoNotThrowOrCreateSession()
    {
        Assert.DoesNotThrow(() => _service.PauseAudio(GuildId));
        Assert.DoesNotThrow(() => _service.ResumeAudio(GuildId));
        Assert.DoesNotThrow(() => _service.StopAudio(GuildId));
        Assert.That(_sessions, Is.Empty);
    }

    private static Process StartSource()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("These process-stream tests require /bin/cat (macOS or Linux).");

        return Process.Start(new ProcessStartInfo("/bin/cat")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;
    }

    private sealed class CancelAfterWriteStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);
            cancellation.Cancel();
        }
    }

    private sealed class UnusedTrackCache : ITrackCacheService
    {
        public bool IsTrackCached(Track track) => throw new NotSupportedException();
        public string GetCachedTrackPath(Track track) => throw new NotSupportedException();
        public string GetCachedTrackPath(string trackReference) => throw new NotSupportedException();
        public string GetDownloadCachePath(Track track) => throw new NotSupportedException();
    }
}
