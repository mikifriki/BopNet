namespace BopNet.Services;

public interface IMusicQueueService
{
    public void AddMusicQueue(ulong guildId, string audioUrl);
    public string? GetNextTrack(ulong guildId);
    public void ClearMusicQueue(ulong guildId);
    public bool HasNextTrack(ulong guildId);
}