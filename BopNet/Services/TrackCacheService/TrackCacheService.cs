using BopNet.Models;

namespace BopNet.Services.TrackCacheService;

public class TrackCacheService : ITrackCacheService {
	private const string DefaultCacheDirectory = "tracks";
	private readonly string _cacheDirectory;

	public TrackCacheService() : this(DefaultCacheDirectory) {}

	public TrackCacheService(string cacheDirectory) {
		_cacheDirectory = cacheDirectory;
	}

	public bool IsTrackCached(Track track) => File.Exists(GetCachedTrackPath(track));

	public string GetCachedTrackPath(Track track) {
		return track.FilePath is null ? GetCachedTrackPath(track.Reference) : EnsureCachePath(track.FilePath);
	}

	public string GetCachedTrackPath(string trackReference) {
		ValidateReference(trackReference);
		return EnsureCachePath(Path.Combine(_cacheDirectory, trackReference + ".final"));
	}

	public string GetDownloadCachePath(Track track) {
		ValidateReference(track.Reference);
		Directory.CreateDirectory(_cacheDirectory);
		return EnsureCachePath(Path.Combine(_cacheDirectory, $"{track.Reference}.{Guid.NewGuid():N}.part"));
	}

	private static void ValidateReference(string reference) {
		if (string.IsNullOrEmpty(reference) || !reference.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
			throw new ArgumentException("Invalid track reference.", nameof(reference));
	}

	private string EnsureCachePath(string path) {
		var fullPath = Path.GetFullPath(path);
		var relativePath = Path.GetRelativePath(Path.GetFullPath(_cacheDirectory), fullPath);
		if (Path.IsPathRooted(relativePath) || relativePath == ".."
		                                    || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			throw new ArgumentException("Track path must be inside the cache directory.", nameof(path));
		return path;
	}
}
