using System.Diagnostics;

namespace BopNet.Services;

public interface IAudioService
{
    public Task StartAudio(ulong guildId, string inputUrl);
    string? GetAudioUrl(string videoUrl);
    void PauseAudio(ulong guildId);
    void StopAudio(ulong guildId);
    bool IsAudioPlaying(ulong guildId);
    Process? GetAudioProcess(ulong guildId);
    public string? GetPlaybackTimestamp(ulong guildId);
}