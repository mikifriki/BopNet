namespace BopNet.Services;

public class MusicQueueService : IMusicQueueService
{
    private readonly Dictionary<ulong, LinkedList<string>> _musicQueue = new();

    public void AddMusicQueue(ulong? guildId, string audioUrl)
    {
        if (guildId == null) return;
        var hasAudioUrl = _musicQueue.TryGetValue(guildId.Value, out var musicQueue);
        if (!hasAudioUrl)
        {
            var musicList = new LinkedList<string>();
            musicList.AddLast(audioUrl);
            _musicQueue.Add(guildId.Value, musicList);
            return;
        }

        musicQueue?.AddLast(audioUrl);
        musicQueue?.AddLast(audioUrl);
    }

    public LinkedList<string>? GetMusicQueue(ulong? guildId) =>
        guildId == null ? null : _musicQueue.GetValueOrDefault(guildId.Value);

    public string? GetNextTrack(ulong guildId)
    {
        if (!_musicQueue.TryGetValue(guildId, out var list) || list.Count == 0) return null;

        var nextTrack = list.First!.Value;
        list.RemoveFirst();
        return nextTrack;
    }

    public void ClearMusicQueue(ulong guildId)
    {
        _musicQueue.Remove(guildId);
    }
}