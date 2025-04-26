using System.Diagnostics;
using System.Text;
using BopNet.Models;

namespace BopNet.Services;

public class AudioService : IAudioService
{
    private readonly Dictionary<ulong, GuildAudio> _ffmpegProcesses = new();

    public Task StartAudio(ulong guildId, string inputUrl)
    {
        StopAudio(guildId);
        var ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-i \"{inputUrl}\" -ar 48000 -ac 2 -map 0:a -c:a pcm_s16le -f tee \"[f=s16le]pipe:1|[f=wav]output.wav\" -progress pipe:2 -nostats",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, // <--- important
                StandardErrorEncoding = Encoding.UTF8
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
        ffmpeg.BeginErrorReadLine();
        audioProcess.AudioProcess = ffmpeg;
        _ffmpegProcesses.Add(guildId, audioProcess);
        return Task.CompletedTask;
    }

    public string? GetPlaybackTimestamp(ulong guildId) =>
        _ffmpegProcesses.TryGetValue(guildId, out var audio) ? audio.TimeStamp : null;

    // Returns URL for which will be used for streaming
    public string? GetAudioUrl(string videoUrl)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = $"-f bestaudio -g \"{videoUrl}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    }

    public void PauseAudio(ulong guildId)
    {
        try
        {
            if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)) return;
            audio?.AudioProcess?.Kill();
            audio?.AudioProcess?.Dispose();
        }
        catch (InvalidOperationException e)
        {
            // FFMPEG is killed by this point
        }
    }

    public void StopAudio(ulong guildId)
    {
        try
        {
            if (!_ffmpegProcesses.TryGetValue(guildId, out var ffmpegProcess)) return;
            ffmpegProcess?.AudioProcess?.Kill();
            ffmpegProcess?.AudioProcess?.Dispose();
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
            return audio!.AudioProcess!.HasExited;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public Process? GetAudioProcess(ulong guildId) =>
        _ffmpegProcesses.TryGetValue(guildId, out var audio) ? audio.AudioProcess! : null;
}