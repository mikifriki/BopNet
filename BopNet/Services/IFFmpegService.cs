using System.Diagnostics;

namespace BopNet.Services;

public interface IFFmpegService
{
    Process? StartFFmpeg(ulong? guildId, string inputUrl);
    void StopFFmpeg(ulong? guildId);
    bool IsFFmpegRunning(ulong? guildId);
    Process? GetFFmpegProcess(ulong? guildId);
}