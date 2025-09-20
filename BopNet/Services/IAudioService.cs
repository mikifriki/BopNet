namespace BopNet.Services;

using Models;

public interface IAudioService
{
    public Task StartAudio(ulong guildId, Track inputUrl, CancellationToken token);
    public Task StreamToDiscordAsync(Stream discordOut, ulong guildId, CancellationToken token);
    void PauseAudio(ulong guildId);
    public void ResumeAudio(ulong guildId);
    void StopAudio(ulong guildId);
}