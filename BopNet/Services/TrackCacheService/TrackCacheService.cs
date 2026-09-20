using BopNet.Models;

namespace BopNet.Services.TrackCacheService;

public class TrackCacheService : ITrackCacheService
{
    private const string DefaultCacheDirectory = "tracks";
    private readonly string _cacheDirectory;

    public TrackCacheService() : this(DefaultCacheDirectory)
    {
    }

    public TrackCacheService(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
    }

    public bool IsTrackCached(Track track) => File.Exists(GetCachedTrackPath(track));

    public string GetCachedTrackPath(Track track)
    {
        return track.FilePath ?? GetCachedTrackPath(track.Reference);
    }

    public string GetCachedTrackPath(string trackReference)
    {
        return $"{_cacheDirectory}/{trackReference}.final";
    }

    public string GetDownloadCachePath(Track track)
    {
        Directory.CreateDirectory(_cacheDirectory);
        return $"{_cacheDirectory}/{track.Reference}.part";
    }
}
