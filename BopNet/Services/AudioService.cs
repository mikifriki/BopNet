using System.Diagnostics;
using BopNet.Models;

namespace BopNet.Services;

public class AudioService : IAudioService
{
    private readonly Dictionary<ulong, GuildAudio> _ffmpegProcesses = new();

    public async Task StartAudio(ulong guildId, string inputUrl)
    {
        string tempFilePath = Path.Combine(Path.GetTempPath(), $"audio_{guildId}.tmp");

        var ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i pipe:0 -f s16le -ar 48000 -ac 2 pipe:1",
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
                Arguments = $"-o \"{tempFilePath}\" -f bestaudio \"{inputUrl}\"",
                RedirectStandardOutput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var audioProcess = new GuildAudio();
        ffmpeg.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data is null || !e.Data.StartsWith("out_time="))
                return;

            audioProcess.TimeStamp = e.Data["out_time=".Length..].Trim();
        };

        ffmpeg.Start();
        await Task.Delay(100);
        ytDlpProcess.Start();
        audioProcess.Ffmpeg = ffmpeg;
        audioProcess.Ytdl = ytDlpProcess;
        _ffmpegProcesses.Add(guildId, audioProcess);

        var output = ffmpeg.StandardInput.BaseStream;

        _ = PipeAsync(tempFilePath, output, audioProcess);
    }

    public async Task StreamToDiscordAsync(Stream discordOut, CancellationToken token, ulong guildId)
    {
        if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)) return;
        var baseStream = audio.Ffmpeg?.StandardOutput.BaseStream;
        if (baseStream is null) return;

        var buffer = new byte[audio.BufferSize]; // 20ms of 48kHz stereo 16-bit
        var silence = new byte[audio.BufferSize];

        while (!token.IsCancellationRequested)
        {
            var data = audio.Paused ? silence : buffer;
            if (!audio.Paused)
            {
                var bytesRead = 0;
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

    private static async Task PipeAsync(string filePath, Stream output, GuildAudio audio)
    {
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[2048];

        while (true)
        {
            try
            {
                if (audio.Ffmpeg!.HasExited) break;
            }
            catch (Exception e)
            {
                break;
            }

            if (fileStream.Position < fileStream.Length)
            {
                var bytesRead = await fileStream.ReadAsync(buffer);
                if (bytesRead <= 0)
                {
                    await Task.Delay(100);
                    continue;
                }

                await output.WriteAsync(buffer.AsMemory(0, bytesRead));
                await output.FlushAsync();
            }
            else
            {
                await Task.Delay(100);
            }
        }
    }

    public void ResumeAudio(ulong guildId)
    {
        if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)) return;
        audio.ReleaseLock();
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
            audio?.Ffmpeg?.Kill();
            audio?.Ffmpeg?.Dispose();
            audio?.Ytdl?.Kill();
            audio?.Ytdl?.Dispose();
            _ffmpegProcesses.Remove(guildId);
        }
        catch (InvalidOperationException e)
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
        catch (Exception e)
        {
            return false;
        }
    }
}