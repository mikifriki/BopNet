using System.Diagnostics;
using BopNet.Models;

namespace BopNet.Services;

public class AudioService : IAudioService
{
    private readonly Dictionary<ulong, GuildAudio> _ffmpegProcesses = new();

    public async Task StartAudio(ulong guildId, string inputUrl, CancellationToken token)
    {
        var ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-re -i pipe:0 -f s16le -ar 48000 -ac 2 pipe:1",
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
                Arguments = $"-o - -f bestaudio \"{inputUrl}\"",
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

    public void ResumeAudio(ulong guildId)
    {
        if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)) return;
        audio.Paused = false;
    }

    public void PauseAudio(ulong guildId)
    {
        if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)) return;
        audio.Paused = true;
    }

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
        catch (InvalidOperationException)
        {
            // FFMPEG is killed by this point
        }
    }

    public bool IsAudioPlaying(ulong guildId)
    {
        try
        {
            _ffmpegProcesses.TryGetValue(guildId, out var audio);
            return audio!.Ffmpeg!.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }
}