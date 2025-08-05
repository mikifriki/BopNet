namespace BopNet.Helpers;

public class UrlFilter {
	
	private const string MarkerRegular = "watch?v=";
	private const string MarkerShort = ".be/";
	
	public string GetVideoIdFromUrl(string url) {
		var i = url.IndexOf(MarkerRegular, StringComparison.Ordinal);
		if (i != -1){
			return i >= 0 ? url[(i + MarkerRegular.Length)..] : "";
		}
		i = url.IndexOf(MarkerShort, StringComparison.Ordinal);
		return i >= 0 ? url[(i + MarkerShort.Length)..] : "";
	}
}
