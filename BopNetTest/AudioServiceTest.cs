using System.Diagnostics;
using System.Reflection;
using BopNet.Models;
using BopNet.Services.AudioService;
using BopNet.Services.TrackCacheService;

namespace BopNetTest;

[NonParallelizable]
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task Playback_DrainsLargeStderrAndReadsProgress(bool cached)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The controlled media processes require a POSIX shell.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"bopnet-audio-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            // Fill stderr before producing audio: playback hangs unless it is drained.
            var ffmpegPath = Path.Combine(directory, "ffmpeg");
            await File.WriteAllTextAsync(ffmpegPath, """
                #!/bin/sh
                set -eu
                input=
                progress=
                while [ "$#" -gt 0 ]; do
                    case "$1" in
                        -i) shift; input=$1 ;;
                        -progress) shift; progress=$1 ;;
                    esac
                    shift
                done
                test "$progress" = pipe:2
                i=0
                while [ "$i" -lt 16384 ]; do
                    printf '%s\n' 'diagnostic padding to exceed the stderr pipe capacity before any audio is emitted' >&2
                    i=$((i + 1))
                done
                if [ "$input" = pipe:0 ]; then /bin/cat; else /bin/cat "$input"; fi
                printf 'out_time=00:00:01.000000\nprogress=end\n' >&2
                """);
            var downloaderPath = Path.Combine(directory, "yt-dlp");
            await File.WriteAllTextAsync(downloaderPath, "#!/bin/sh\nprintf audio-payload\n");
            var executableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            File.SetUnixFileMode(ffmpegPath, executableMode);
            File.SetUnixFileMode(downloaderPath, executableMode);
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + originalPath);

            var cache = new TrackCacheService(Path.Combine(directory, "tracks"));
            _service = new AudioService(cache);
            _sessions = (Dictionary<ulong, GuildAudio>)typeof(AudioService)
                .GetField("_ffmpegProcesses", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(_service)!;
            var track = new Track { Reference = "test", FullUrl = "https://example.invalid/audio" };
            if (cached)
            {
                Directory.CreateDirectory(Path.Combine(directory, "tracks"));
                await File.WriteAllTextAsync(cache.GetCachedTrackPath(track), "audio-payload");
                await _service.StartCachedAudio(GuildId, track, timeout.Token);
            }
            else
            {
                await _service.StartAudio(GuildId, track, timeout.Token);
            }

            using var destination = new MemoryStream();
            await _service.StreamToDiscordAsync(destination, GuildId, timeout.Token);
            var session = _sessions[GuildId];
            await session.Ffmpeg!.WaitForExitAsync(timeout.Token);
            // PipeAsync finalizes the downloaded cache just after closing FFmpeg's stdin.
            while (!File.Exists(cache.GetCachedTrackPath(track)))
                await Task.Delay(10, timeout.Token);

            Assert.Multiple(() =>
            {
                Assert.That(session.Ffmpeg.ExitCode, Is.Zero);
                Assert.That(System.Text.Encoding.UTF8.GetString(destination.ToArray()), Is.EqualTo("audio-payload"));
                Assert.That(session.TimeStamp, Is.EqualTo("00:00:01.000000"));
            });
        }
        finally
        {
            timeout.Cancel();
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (_sessions.TryGetValue(GuildId, out var session))
            {
                foreach (var process in new[] { session.Ffmpeg, session.Ytdl })
                {
                    if (process is null) continue;
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    process.Dispose();
                }
            }
            Directory.Delete(directory, recursive: true);
        }
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
