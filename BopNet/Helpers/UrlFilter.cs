namespace BopNet.Helpers;

public class UrlFilter {
	public static string GetVideoIdFromUrl(string url) {
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
		    || uri.Scheme is not ("https" or "http")
		    || uri.UserInfo.Length != 0 || !uri.IsDefaultPort)
			return "";

		string videoId;
		if (uri.Host is "youtu.be" or "www.youtu.be"){
			videoId = uri.AbsolutePath.Trim('/');
		}
		else if (uri.Host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com"){
			if (uri.AbsolutePath == "/watch"){
				var values = uri.Query.TrimStart('?').Split('&')
					.Where(parameter => parameter.StartsWith("v=", StringComparison.Ordinal)).ToArray();
				if (values.Length != 1) return "";
				videoId = values[0][2..];
			}
			else{
				var segments = uri.AbsolutePath.Trim('/').Split('/');
				if (segments.Length != 2 || segments[0] is not ("shorts" or "live" or "embed")) return "";
				videoId = segments[1];
			}
		}
		else{
			return "";
		}

		return videoId.Length == 11 && videoId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
			? videoId
			: "";
	}
}
