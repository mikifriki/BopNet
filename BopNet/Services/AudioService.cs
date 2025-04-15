using System.Diagnostics;

namespace BopNet.Services;

public class AudioService : IAudioService
{
    private Process? _ffmpegProcesses = new();

    public Process? StartAudio(ulong? guildId, string inputUrl)
    {
        StopAudio(guildId);
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments =
                $"-i \"{inputUrl}\" -ar 48000 -ac 2 -map 0:a -c:a pcm_s16le -f tee \"[f=s16le]pipe:1|[f=wav]output.wav\" -loglevel error",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var ffmpegProcess = Process.Start(psi);

        if (ffmpegProcess == null) return null;
        _ffmpegProcesses = ffmpegProcess;
        return ffmpegProcess;
    }

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

    public void StopAudio(ulong? guildId)
    {
        try
        {
            if (_ffmpegProcesses == null) return;
            _ffmpegProcesses.Kill();
            _ffmpegProcesses.Dispose();
            _ffmpegProcesses = null;
        }
        catch (InvalidOperationException e)
        {
            // FFMPEG is killed by this point
        }
    }

    public bool IsAudioPlaying(ulong? guildId)
    {
        try
        {
            return _ffmpegProcesses!.HasExited;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public Process? GetAudioProcess(ulong? guildId) => _ffmpegProcesses;
}