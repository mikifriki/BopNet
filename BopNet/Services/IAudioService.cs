namespace BopNet.Services;

public interface IAudioService
{
    public Task StartAudio(ulong guildId, string inputUrl, CancellationToken token);
    public Task StreamToDiscordAsync(Stream discordOut, ulong guildId, CancellationToken token);
    void PauseAudio(ulong guildId);
    public void ResumeAudio(ulong guildId);
    void StopAudio(ulong guildId);
}