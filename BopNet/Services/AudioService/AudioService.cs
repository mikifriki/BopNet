using System.Diagnostics;
using BopNet.Models;
using BopNet.Services.TrackCacheService;

namespace BopNet.Services.AudioService;

public class AudioService(ITrackCacheService trackCacheService) : IAudioService
{
    private readonly Dictionary<ulong, GuildAudio> _ffmpegProcesses = new();

    /// <summary>
    /// Starts streaming given url to ffmpeg buffer
    /// </summary>
    /// <param name="guildId">Discord guild ID</param>
    /// <param name="track">Music track which should be read</param>
    /// <param name="token">Cancellation token</param>
    public async Task StartAudio(ulong guildId, Track track, CancellationToken token)
    {
        StopAudio(guildId);
        var ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-progress pipe:2 -nostats -i pipe:0 -f s16le -vn -ar 48000 -ac 2 pipe:1",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        var ytDlpProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"--no-playlist -o - -f bestaudio --no-part \"{track.FullUrl}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var audioProcess = new GuildAudio();

        // Progress shares stderr with diagnostics; keep draining both during playback.
        ffmpeg.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null || !e.Data.StartsWith("out_time="))
                return;

            audioProcess.TimeStamp = e.Data["out_time=".Length..].Trim();
        };

        ffmpeg.Start();
        ffmpeg.BeginErrorReadLine();
        // Add a small delay between ffmpeg and ytdlp to ensure ffmpeg is up and running.
        await Task.Delay(100, token);
        ytDlpProcess.Start();

        audioProcess.Ffmpeg = ffmpeg;
        audioProcess.Ytdl = ytDlpProcess;
        _ffmpegProcesses.Add(guildId, audioProcess);

        var downloadPath = trackCacheService.GetDownloadCachePath(track);
        var cachedPath = trackCacheService.GetCachedTrackPath(track);
        _ = PipeAsync(ytDlpProcess.StandardOutput.BaseStream, ffmpeg.StandardInput.BaseStream,
            downloadPath, cachedPath, audioProcess, token);
    }

    public async Task StartCachedAudio(ulong guildId, Track track, CancellationToken token)
    {
        StopAudio(guildId);
        var ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-progress pipe:2 -nostats -i \"{trackCacheService.GetCachedTrackPath(track)}\" -f s16le -ar 48000 -ac 2 pipe:1",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        var audioProcess = new GuildAudio();

        // Progress shares stderr with diagnostics; keep draining both during playback.
        ffmpeg.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null || !e.Data.StartsWith("out_time="))
                return;

            audioProcess.TimeStamp = e.Data["out_time=".Length..].Trim();
        };

        ffmpeg.Start();
        ffmpeg.BeginErrorReadLine();
        // Add a small delay between ffmpeg and ytdlp to ensure ffmpeg is up and running.
        await Task.Delay(100, token);

        audioProcess.Ffmpeg = ffmpeg;
        _ffmpegProcesses.Add(guildId, audioProcess);
    }

    /// <summary>
    /// Streams the previously started stream into Discord.
    /// </summary>
    /// <param name="discordOut">Discord Stream which awaits input</param>
    /// <param name="guildId">Discord Guild Id</param>
    /// <param name="token">Cancellation token</param>
    public async Task StreamToDiscordAsync(Stream discordOut, ulong guildId, CancellationToken token)
    {
        if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)) return;
        var baseStream = audio.Ffmpeg?.StandardOutput.BaseStream;
        if (baseStream is null) return;

        var buffer = new byte[GuildAudio.BufferSize];
        var silence = new byte[GuildAudio.BufferSize];

        while (!token.IsCancellationRequested)
        {
            var data = audio.Paused ? silence : buffer;
            var bytesToWrite = data.Length;
            if (!audio.Paused)
            {
                int bytesRead;
                try
                {
                    bytesRead = await baseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                }
                catch (IOException)
                {
                    break; // FFMPEG stream closed
                }

                if (bytesRead <= 0) break;
                bytesToWrite = bytesRead;
            }

            await discordOut.WriteAsync(data.AsMemory(0, bytesToWrite), token);
        }
    }

    private static async Task PipeAsync(Stream input, Stream output, string path, string finalPath, GuildAudio audio,
        CancellationToken token)
    {
        var buffer = new byte[GuildAudio.BufferSize];

        await using (var fileStream = File.Create(path))
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (audio.Ffmpeg!.HasExited) break;
                    }
                    catch (Exception)
                    {
                        break;
                    }

                    var bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                    if (bytesRead <= 0) break;

                    var bytes = buffer.AsMemory(0, bytesRead);
                    await fileStream.WriteAsync(bytes, token);
                    await output.WriteAsync(bytes, token);
                }

                if (!token.IsCancellationRequested)
                {
                    await output.FlushAsync(token);
                }
            }
            finally
            {
                await output.DisposeAsync();
            }
        }

        if (File.Exists(path))
        {
            File.Move(path, finalPath, overwrite: true);
        }
    }

    /// <summary>
    /// Resumes Paused audio.
    /// </summary>
    /// <param name="guildId">Discord Guild Id</param>
    public void ResumeAudio(ulong guildId)
    {
        if (!_ffmpegProcesses.TryGetValue(guildId, out var audio))
        {
            Console.WriteLine("Failed to resume audio for Guild: " + guildId);
            return;
        }

        audio.Paused = false;
    }

    /// <summary>
    /// Pauses the currently streamed audio.
    /// </summary>
    /// <param name="guildId">Discord Guild Id</param>
    public void PauseAudio(ulong guildId)
    {
        if (!_ffmpegProcesses.TryGetValue(guildId, out var audio))
        {
            Console.WriteLine("Failed to Pause Audio for Guild: " + guildId);
            return;
        }

        audio.Paused = true;
    }

    /// <summary>
    /// Stops the audio playback
    /// </summary>
    /// <param name="guildId">Discord Guild Id</param>
    public void StopAudio(ulong guildId)
    {
        try
        {
            if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)) return;
            audio.Ffmpeg?.Kill();
            audio.Ffmpeg?.Dispose();
            audio.Ytdl?.Kill();
            audio.Ytdl?.Dispose();
            _ffmpegProcesses.Remove(guildId);
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine("Audio Process already killed: " + e.Message);
            // FFMPEG is killed by this point
        }
    }
}
