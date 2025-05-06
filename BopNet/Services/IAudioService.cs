namespace BopNet.Services;

public interface IAudioService
{
    public Task StartAudio(ulong guildId, string inputUrl);
    public Task StreamToDiscordAsync(Stream discordOut, CancellationToken token, ulong guildId);
    void PauseAudio(ulong guildId);
    public void ResumeAudio(ulong guildId);
    void StopAudio(ulong guildId);
}