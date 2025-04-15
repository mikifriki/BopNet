using System.Diagnostics;

namespace BopNet.Services;

public interface IAudioService
{
    Process? StartAudio(ulong? guildId, string inputUrl);
    string? GetAudioUrl(string videoUrl);
    void StopAudio(ulong? guildId);
    bool IsAudioPlaying(ulong? guildId);
    Process? GetAudioProcess(ulong? guildId);
}