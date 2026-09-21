using System.Collections.Concurrent;
using BopNet.Services.MusicQueueService;

namespace BopNetTest;

public class MusicQueueServiceTest {
	[Test]
	public void ConcurrentEnqueueAndDequeue_PreservesEveryTrackAndGuild() {
		var service = new MusicQueueService();
		Parallel.For(0, 1000, i => service.AddMusicQueue((ulong) (i % 2), i.ToString()));
		var tracks = new ConcurrentBag<string>();
		Parallel.For(0, 8, _ => {
			while (service.GetNextTrack(0) is {} track) tracks.Add(track);
		});
		Assert.Multiple(() => {
			Assert.That(tracks, Is.EquivalentTo(Enumerable.Range(0, 500).Select(i => (i * 2).ToString())));
			Assert.That(service.HasNextTrack(0), Is.False);
			Assert.That(service.HasNextTrack(1), Is.True);
		});
	}

	[Test]
	public void ClearAndRestart_PreservesOtherGuildAndQueueOrder() {
		var service = new MusicQueueService();
		service.AddMusicQueue(1, "first");
		service.AddMusicQueue(2, "other");
		service.ClearMusicQueue(1);
		service.AddMusicQueue(1, "second");
		service.AddMusicQueue(1, "third");
		Assert.Multiple(() => {
			Assert.That(service.GetNextTrack(1), Is.EqualTo("second"));
			Assert.That(service.GetNextTrack(1), Is.EqualTo("third"));
			Assert.That(service.GetNextTrack(1), Is.Null);
			Assert.That(service.GetNextTrack(2), Is.EqualTo("other"));
		});
	}
}
