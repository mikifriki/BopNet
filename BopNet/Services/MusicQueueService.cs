namespace BopNet.Services;

public class MusicQueueService : IMusicQueueService
{
    private readonly Dictionary<ulong, LinkedList<string>> _musicQueue = new();

    /// <summary>
    /// Adds given url to playback queue
    /// </summary>
    /// <param name="guildId">Discord Guild Id</param>
    /// <param name="audioUrl">Audio URL which will be streamed</param>
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

    /// <summary>
    /// Gets next Track and removes the first song from the queue
    /// </summary>
    /// <param name="guildId"></param>
    /// <returns></returns>
    public string? GetNextTrack(ulong guildId)
    {
        if (!_musicQueue.TryGetValue(guildId, out var list) || list.Count == 0) return null;

        var nextTrack = list.First!.Value;
        list.RemoveFirst();
        return nextTrack;
    }

    public bool HasNextTrack(ulong guildId) => _musicQueue.TryGetValue(guildId, out _);

    public void ClearMusicQueue(ulong guildId) => _musicQueue.Remove(guildId);
}