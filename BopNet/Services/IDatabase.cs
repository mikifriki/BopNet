namespace BopNet.Services;

using Models;

public interface IDatabase : IDisposable {
	public void SaveTrack(Track track);
}
