namespace BopNet.Services;

using Models;

public interface IDatabase : IDisposable {
	public Track SaveTrack(Track track);
}
