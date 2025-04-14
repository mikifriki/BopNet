using System.Diagnostics;

namespace BopNet.Services;

public class FFmpegService : IFFmpegService
{
    private Process? _ffmpegProcesses = new();

    public Process? StartFFmpeg(ulong? guildId, string inputUrl)
    {
        StopFFmpeg(guildId);
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

    public void StopFFmpeg(ulong? guildId)
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

    public bool IsFFmpegRunning(ulong? guildId)
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

    public Process? GetFFmpegProcess(ulong? guildId) => _ffmpegProcesses;
}