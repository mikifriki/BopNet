namespace BopNet.Services;

using Models;
using Repository;

public class DataBaseService(BotDbContext dbContext) : IDatabase {

	public IQueryable<Track> Tracks => dbContext.Tracks.AsQueryable();
	
	public void SaveTrack(Track track)
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
	}
	
	
	public void Dispose()
	{
		dbContext.Dispose();
	}
}
