namespace BopNet.Services.DataBaseService;

using Models;
using Repository;

public class DataBaseService(BotDbContext dbContext) : IDatabase
{
    public IQueryable<Track> Tracks => dbContext.Tracks.AsQueryable();

    public Track SaveTrack(Track track)
    {
        var existing = dbContext.Tracks
            .FirstOrDefault(t => t.Reference == track.Reference);

        if (existing is not null) throw new InvalidOperationException($"Track already exists: {existing}");

        dbContext.Tracks.Add(track);
        dbContext.SaveChanges();

        return track;
    }

    public Track? UpdateTrackPlayCount(Track track)
    {
        var existing = dbContext.Tracks
            .FirstOrDefault(t => t.Reference == track.Reference);

        if (existing is null) throw new InvalidOperationException($"Track already exists: {existing}");

        existing.PlayCount += 1;
        dbContext.Tracks.Update(track);
        dbContext.SaveChanges();
        return existing;
    }

    public Track? GetTrack(string trackReference)
    {
        return dbContext.Tracks
            .FirstOrDefault(t => t.Reference == trackReference);
    }


    public void Dispose()
    {
        dbContext.Dispose();
    }
}