using System.Collections.Concurrent;

namespace BopNet.Services.MusicQueueService;

public class MusicQueueService : IMusicQueueService {
	private readonly ConcurrentDictionary<ulong, ConcurrentQueue<string>> _musicQueue = new();

	/// <summary>
	/// Adds given url to playback queue
	/// </summary>
	/// <param name="guildId">Discord Guild Id</param>
	/// <param name="url">Audio URL which will be streamed</param>
	public void AddMusicQueue(ulong guildId, string url) {
		_musicQueue.GetOrAdd(guildId, _ => new ConcurrentQueue<string>()).Enqueue(url);
	}

	/// <summary>
	/// Gets next Track and removes the first song from the queue
	/// </summary>
	/// <param name="guildId"></param>
	/// <returns></returns>
	public string? GetNextTrack(ulong guildId) {
		return _musicQueue.TryGetValue(guildId, out var queue) && queue.TryDequeue(out var track) ? track : null;
	}

	public bool HasNextTrack(ulong guildId) => _musicQueue.TryGetValue(guildId, out var queue) && !queue.IsEmpty;

	public void ClearMusicQueue(ulong guildId) => _musicQueue.TryRemove(guildId, out _);
}
