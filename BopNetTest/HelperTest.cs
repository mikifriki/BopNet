namespace BopNetTest;

using BopNet.Helpers;

public class Tests {
	private UrlFilter _urlFilter;
	
	[SetUp]
	public void Setup() {
		_urlFilter = new UrlFilter();
	}

	[Test]
	public void Test1() {
		const string videoUrl = "https://www.youtube.com/watch?v=JRWox-i6aAk&list=RDJRWox-i6aAk&start_radio=1&rv=JRWox-i6aAk";
		const string videoUrl2 = "https://youtu.be/JRWox-i6aAk&list=RDJRWox-i6aAk&start_radio=1&rv=JRWox-i6aAk";
		const string videoId = "JRWox-i6aAk&list=RDJRWox-i6aAk&start_radio=1&rv=JRWox-i6aAk";

		Assert.That(_urlFilter.GetVideoIdFromUrl(videoUrl), Is.EqualTo(videoId));
		Assert.That(_urlFilter.GetVideoIdFromUrl(videoUrl2), Is.EqualTo(videoId));
	}
}
