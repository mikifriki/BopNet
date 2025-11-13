namespace BopNet.Services.DataBaseService;

using Models;

public interface IDatabase : IDisposable {
	public Track SaveTrack(Track track);
	public Track? UpdateTrackPlayCount(Track track);
	public Track? GetTrack(string trackReference);
}
