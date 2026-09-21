using System.ComponentModel;
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
    private IDictionary<ulong, GuildAudio> _sessions = null!;

    [SetUp]
    public void SetUp() => SetService(new UnusedTrackCache());

    private void SetService(ITrackCacheService cache)
    {
        _service = new AudioService(cache);
        // Seed playback without downloading audio or requiring FFmpeg.
        _sessions = (IDictionary<ulong, GuildAudio>)typeof(AudioService)
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
        Assert.DoesNotThrowAsync(() => _service.StopAudio(GuildId));
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
            SetService(cache);
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
            Assert.Multiple(() =>
            {
                Assert.That(cache.IsTrackCached(track), Is.True);
                Assert.That(session.Ffmpeg.ExitCode, Is.Zero);
                Assert.That(System.Text.Encoding.UTF8.GetString(destination.ToArray()), Is.EqualTo("audio-payload"));
                Assert.That(session.TimeStamp, Is.EqualTo("00:00:01.000000"));
            });
        }
        finally
        {
            timeout.Cancel();
            Environment.SetEnvironmentVariable("PATH", originalPath);
            await _service.StopAudio(GuildId);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task StopAudio_WhenFfmpegWasNotStarted_StillStopsDownloaderAndRemovesSession()
    {
        using var source = StartSource();
        using var monitor = Process.GetProcessById(source.Id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _sessions.Add(GuildId, new GuildAudio { Ffmpeg = new Process(), Ytdl = source });
        try
        {
            await _service.StopAudio(GuildId);
            await monitor.WaitForExitAsync(timeout.Token);
            Assert.That(_sessions, Is.Empty);
            Assert.DoesNotThrowAsync(() => _service.StopAudio(GuildId));
        }
        finally
        {
            if (!monitor.HasExited) monitor.Kill(entireProcessTree: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Startup_WhenMediaToolIsMissing_RemovesFailedSession(bool cached)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The controlled media process requires a POSIX shell.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"bopnet-startup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            // For downloaded audio, FFmpeg starts successfully before yt-dlp fails to start.
            if (!cached)
            {
                var executable = Path.Combine(directory, "ffmpeg");
                File.WriteAllText(executable, "#!/bin/sh\nexec /bin/cat\n");
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Environment.SetEnvironmentVariable("PATH", directory);
            SetService(new TrackCacheService(Path.Combine(directory, "tracks")));
            var track = new Track { Reference = "test", FullUrl = "https://example.invalid/audio" };

            Assert.ThrowsAsync<Win32Exception>(async () =>
            {
                if (cached) await _service.StartCachedAudio(GuildId, track, CancellationToken.None);
                else await _service.StartAudio(GuildId, track, CancellationToken.None);
            });
            Assert.That(_sessions, Is.Empty);
        }
        finally
        {
            await _service.StopAudio(GuildId);
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SkipTrack_CompletesStreamingAndAllowsNextTrack(bool paused)
    {
        using var source = StartSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var destination = new WaitForCancellationStream();
        _sessions.Add(GuildId, new GuildAudio { Ffmpeg = source, Paused = paused });
        var streaming = _service.StreamToDiscordAsync(destination, GuildId, timeout.Token);
        try
        {
            if (paused) await destination.Started.Task.WaitAsync(timeout.Token);
            await _service.StopAudio(GuildId).WaitAsync(timeout.Token);
            await streaming.WaitAsync(timeout.Token);
            Assert.That(timeout.IsCancellationRequested, Is.False);

            using var nextSource = StartSource();
            _sessions.Add(GuildId, new GuildAudio { Ffmpeg = nextSource });
            using var nextOutput = new MemoryStream();
            var next = _service.StreamToDiscordAsync(nextOutput, GuildId, timeout.Token);
            await nextSource.StandardInput.WriteAsync("next track");
            nextSource.StandardInput.Close();
            await next.WaitAsync(timeout.Token);
            Assert.That(System.Text.Encoding.UTF8.GetString(nextOutput.ToArray()), Is.EqualTo("next track"));
            await _service.StopAudio(GuildId);
        }
        finally
        {
            timeout.Cancel();
            await _service.StopAudio(GuildId);
            await streaming;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SameVideoInTwoGuilds_CompletesWithoutSharingPartialFiles(bool skipFirst)
    {
        using var media = new ControlledMediaTools();
        var cache = new TrackCacheService(Path.Combine(media.DirectoryPath, "tracks"));
        SetService(cache);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var track = new Track { Reference = "same-video", FullUrl = "https://example.invalid/audio" };
        using var firstOutput = new MemoryStream();
        using var secondOutput = new MemoryStream();
        Task first = Task.CompletedTask;
        Task second = Task.CompletedTask;
        try
        {
            await _service.StartAudio(GuildId, track, timeout.Token);
            first = _service.StreamToDiscordAsync(firstOutput, GuildId, timeout.Token);
            await _service.StartAudio(GuildId + 1, track, timeout.Token);
            second = _service.StreamToDiscordAsync(secondOutput, GuildId + 1, timeout.Token);
            await media.WaitForDownloaders(2, timeout.Token);

            if (skipFirst)
                await _service.StopAudio(GuildId).WaitAsync(timeout.Token);
            media.ReleaseDownloads();
            await Task.WhenAll(first, second).WaitAsync(timeout.Token);

            Assert.Multiple(() =>
            {
                if (!skipFirst)
                    Assert.That(System.Text.Encoding.UTF8.GetString(firstOutput.ToArray()), Is.EqualTo("audio-payload"));
                Assert.That(System.Text.Encoding.UTF8.GetString(secondOutput.ToArray()), Is.EqualTo("audio-payload"));
                Assert.That(File.ReadAllText(cache.GetCachedTrackPath(track)), Is.EqualTo("audio-payload"));
                Assert.That(Directory.GetFiles(Path.Combine(media.DirectoryPath, "tracks"), "*.part"), Is.Empty);
            });
        }
        finally
        {
            timeout.Cancel();
            media.ReleaseDownloads();
            await _service.StopAudio(GuildId);
            await _service.StopAudio(GuildId + 1);
            await Task.WhenAll(first, second);
        }
    }

    [Test]
    public async Task DownloadFileOpenFailure_IsObservedAndClosesFfmpegInput()
    {
        using var media = new ControlledMediaTools();
        SetService(new MissingDownloadDirectory(media.DirectoryPath));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _service.StartAudio(GuildId, new Track { Reference = "test", FullUrl = "https://example.invalid" }, timeout.Token);
            Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                _service.StreamToDiscordAsync(Stream.Null, GuildId, timeout.Token).WaitAsync(timeout.Token));
            await _sessions[GuildId].Ffmpeg!.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            timeout.Cancel();
            media.ReleaseDownloads();
            await _service.StopAudio(GuildId);
        }
    }

    [Test]
    public async Task DiscordWriteFailure_StopsDownloadWithoutPublishingCache()
    {
        using var media = new ControlledMediaTools();
        var cache = new TrackCacheService(Path.Combine(media.DirectoryPath, "tracks"));
        SetService(cache);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var destination = new FailingWriteStream();
        var track = new Track { Reference = "test", FullUrl = "https://example.invalid/audio" };
        try
        {
            await _service.StartAudio(GuildId, track, timeout.Token);
            var exception = Assert.ThrowsAsync<IOException>(() =>
                _service.StreamToDiscordAsync(destination, GuildId, timeout.Token).WaitAsync(timeout.Token));
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo("Controlled voice write failure"));
                Assert.That(cache.IsTrackCached(track), Is.False);
                Assert.That(Directory.GetFiles(Path.Combine(media.DirectoryPath, "tracks")), Is.Empty);
            });
        }
        finally
        {
            timeout.Cancel();
            await _service.StopAudio(GuildId);
        }
    }

    private sealed class FailingWriteStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("Controlled voice write failure"));
    }

    private sealed class WaitForCancellationStream : MemoryStream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class ControlledMediaTools : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"bopnet-concurrency-test-{Guid.NewGuid():N}");
        private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");

        public ControlledMediaTools()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("The controlled media processes require a POSIX shell.");
                return;
            }
            Directory.CreateDirectory(DirectoryPath);
            WriteTool("ffmpeg", "#!/bin/sh\nexec /bin/cat\n");
            WriteTool("yt-dlp", """
                #!/bin/sh
                directory=${0%/*}
                printf audio-payload
                touch "$directory/started-$$"
                while [ ! -f "$directory/release" ]; do sleep 0.01; done
                """);
            Environment.SetEnvironmentVariable("PATH", DirectoryPath + Path.PathSeparator + _originalPath);
        }

        private void WriteTool(string name, string content)
        {
            var path = Path.Combine(DirectoryPath, name);
            File.WriteAllText(path, content);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public async Task WaitForDownloaders(int count, CancellationToken token)
        {
            while (Directory.GetFiles(DirectoryPath, "started-*").Length < count)
                await Task.Delay(10, token);
        }

        public void ReleaseDownloads() => File.WriteAllText(Path.Combine(DirectoryPath, "release"), "");

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", _originalPath);
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private sealed class MissingDownloadDirectory(string directory) : ITrackCacheService
    {
        public bool IsTrackCached(Track track) => false;
        public string GetCachedTrackPath(Track track) => GetCachedTrackPath(track.Reference);
        public string GetCachedTrackPath(string reference) => Path.Combine(directory, reference + ".final");
        public string GetDownloadCachePath(Track track) => Path.Combine(directory, "missing", track.Reference + ".part");
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
