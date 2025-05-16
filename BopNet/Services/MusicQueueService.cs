namespace BopNet.Services;

public class MusicQueueService : IMusicQueueService
{
    private readonly Dictionary<ulong, LinkedList<string>> _musicQueue = new();

    public void AddMusicQueue(ulong guildId, string audioUrl)
    {
        var hasAudioUrl = _musicQueue.TryGetValue(guildId, out var musicQueue);
        if (!hasAudioUrl)
        {
            var musicList = new LinkedList<string>();
            musicList.AddLast(audioUrl);
            _musicQueue.Add(guildId, musicList);
            return;
        }

        musicQueue?.AddLast(audioUrl);
    }

    public LinkedList<string>? GetMusicQueue(ulong guildId) => _musicQueue.GetValueOrDefault(guildId);

    public string? GetNextTrack(ulong guildId)
    {
        if (!_musicQueue.TryGetValue(guildId, out var list) || list.Count == 0) return null;

        var nextTrack = list.First!.Value;
        list.RemoveFirst();
        return nextTrack;
    }

    public bool HasNextTrack (ulong guildId) => _musicQueue.TryGetValue(guildId, out _);
    
    public void ClearMusicQueue(ulong guildId) => _musicQueue.Remove(guildId);
}