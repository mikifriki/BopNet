using System.Diagnostics;
using BopNet.Models;

namespace BopNet.Services;

public class AudioService : IAudioService
{
    private readonly Dictionary<ulong, GuildAudio> _ffmpegProcesses = new();

    /// <summary>
    /// Starts streaming given url to ffmpeg buffer
    /// </summary>
    /// <param name="guildId">Discord guild ID</param>
    /// <param name="inputUrl">Music track which should be read</param>
    /// <param name="token">Cancellation token</param>
    public async Task StartAudio(ulong guildId, string inputUrl, CancellationToken token)
    {
        StopAudio(guildId);
        var ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i pipe:0 -f s16le -vn -ar 48000 -ac 2 pipe:1",
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
                Arguments = $"--no-playlist -o - -f bestaudio --no-part \"{inputUrl}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var audioProcess = new GuildAudio();

        // Since ffmpeg redirects its timestamp into ErrorData then we can get it for future use.
        ffmpeg.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null || !e.Data.StartsWith("out_time="))
                return;

            audioProcess.TimeStamp = e.Data["out_time=".Length..].Trim();
        };

        ffmpeg.Start();
        // Add a small delay between ffmpeg and ytdlp to ensure ffmpeg is up and running.
        await Task.Delay(100, token);
        ytDlpProcess.Start();

        audioProcess.Ffmpeg = ffmpeg;
        audioProcess.Ytdl = ytDlpProcess;
        _ffmpegProcesses.Add(guildId, audioProcess);

        _ = PipeAsync(ytDlpProcess.StandardOutput.BaseStream, ffmpeg.StandardInput.BaseStream, audioProcess, token);
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
            }

            await discordOut.WriteAsync(data.AsMemory(0, data.Length), token);
        }
    }

    private async static Task PipeAsync(Stream input, Stream output, GuildAudio audio, CancellationToken token)
    {
        var buffer = new byte[GuildAudio.BufferSize];

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

            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            if (read <= 0) break;

            await output.WriteAsync(buffer.AsMemory(0, read), token);
            await output.FlushAsync(token);
        }

        await output.DisposeAsync();
    }

    /// <summary>
    /// Resumes Paused audio.
    /// </summary>
    /// <param name="guildId">Discord Guild Id</param>
    public void ResumeAudio(ulong guildId)
    {
        if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)){
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
        if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)){
            Console.WriteLine("Failed to Pause Audio for Guild: " + guildId);
            return;
        };
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