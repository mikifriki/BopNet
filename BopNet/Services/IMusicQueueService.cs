namespace BopNet.Services;

public interface IMusicQueueService
{
    public void AddMusicQueue(ulong guildId, string audioUrl);
    //For debug
    public LinkedList<string>? GetMusicQueue(ulong guildId);
    public string? GetNextTrack(ulong guildId);
    public void ClearMusicQueue(ulong guildId);
    public bool HasNextTrack(ulong guildId);
}