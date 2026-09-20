using BopNet.Models;

namespace BopNet.Services.TrackCacheService;

public interface ITrackCacheService
{
    bool IsTrackCached(Track track);
    string GetCachedTrackPath(Track track);
    string GetCachedTrackPath(string trackReference);
    string GetDownloadCachePath(Track track);
}
