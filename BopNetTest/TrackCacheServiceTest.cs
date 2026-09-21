using BopNet.Models;
using BopNet.Services.TrackCacheService;

namespace BopNetTest;

public class TrackCacheServiceTest {
	private string _directory = null!;
	private TrackCacheService _service = null!;

	[SetUp]
	public void SetUp() {
		_directory = Path.Combine(Path.GetTempPath(), $"bopnet-cache-test-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_directory);
		_service = new TrackCacheService(_directory);
	}

	[TearDown]
	public void TearDown() => Directory.Delete(_directory, recursive: true);

	[TestCase("")]
	[TestCase("../outside")]
	[TestCase("..\\outside")]
	[TestCase("/absolute")]
	[TestCase("id/../../outside")]
	[TestCase("id?query")]
	public void CachePaths_RejectUnsafeReferences(string reference) {
		Assert.Throws<ArgumentException>(() => _service.GetCachedTrackPath(reference));
		Assert.Throws<ArgumentException>(() => _service.GetDownloadCachePath(new Track { Reference = reference }));
	}

	[Test]
	public void DefaultCachePaths_RemainRelativeForDatabasePortability() {
		var path = new TrackCacheService().GetCachedTrackPath("JRWox-i6aAk");
		Assert.That(path, Is.EqualTo(Path.Combine("tracks", "JRWox-i6aAk.final")));
		Assert.That(Path.IsPathRooted(path), Is.False);
	}

	[Test]
	public void CachedTrack_RejectsPersistedPathOutsideCache() {
		var track = new Track { FilePath = Path.Combine(_directory, "..", "outside.final") };
		Assert.Throws<ArgumentException>(() => _service.IsTrackCached(track));
		track.FilePath = Path.Combine(_directory + "-other", "track.final");
		Assert.Throws<ArgumentException>(() => _service.GetCachedTrackPath(track));
	}

	[Test]
	public void CachedTrack_PreservesExistingFilePathAndContents() {
		var path = Path.Combine(_directory, "legacy&list=playlist.final");
		File.WriteAllText(path, "cached audio");
		var track = new Track { Reference = "legacy&list=playlist", FilePath = path };
		Assert.Multiple(() => {
			Assert.That(_service.IsTrackCached(track), Is.True);
			Assert.That(_service.GetCachedTrackPath(track), Is.EqualTo(path));
			Assert.That(File.ReadAllText(path), Is.EqualTo("cached audio"));
		});
	}
}
