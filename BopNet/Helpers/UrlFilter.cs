namespace BopNet.Helpers;

public class UrlFilter {
	
	private const string Marker = "watch?v=";
	
	public string GetVideoIdFromUrl(string url) {
		var i = url.IndexOf(Marker, StringComparison.Ordinal);
		return i >= 0 ? url[(i + Marker.Length)..] : "";
	}
}
