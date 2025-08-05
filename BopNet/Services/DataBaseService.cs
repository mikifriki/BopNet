namespace BopNet.Services;

using Models;
using Repository;

public class DataBaseService(BotDbContext dbContext) : IDatabase {

	public IQueryable<Track> Tracks => dbContext.Tracks.AsQueryable();
	
	public Track SaveTrack(Track track)
	{
		var existing = dbContext.Tracks
			.FirstOrDefault(t => t.Reference == track.Reference);

		if (existing is null)
		{
			dbContext.Tracks.Add(track);
		}
		else{
			existing.PlayCount += 1;
		}

		dbContext.SaveChanges();
		return track;
	}
	
	
	public void Dispose()
	{
		dbContext.Dispose();
	}
}
