using BopNet.Helpers;

namespace BopNetTest;

public class HelperTest {
	[TestCase("https://www.youtube.com/watch?v=JRWox-i6aAk&list=playlist&t=20")]
	[TestCase("https://youtube.com/watch?t=20&v=JRWox-i6aAk#fragment")]
	[TestCase("https://youtu.be/JRWox-i6aAk?si=share-code")]
	[TestCase("http://m.youtube.com/watch?v=JRWox-i6aAk")]
	[TestCase("https://music.youtube.com/watch?v=JRWox-i6aAk")]
	[TestCase("https://www.youtube.com/shorts/JRWox-i6aAk")]
	[TestCase("https://www.youtube.com/live/JRWox-i6aAk")]
	[TestCase("https://www.youtube.com/embed/JRWox-i6aAk")]
	public void GetVideoIdFromUrl_ReturnsOnlyTheVideoId(string url) {
		Assert.That(new UrlFilter().GetVideoIdFromUrl(url), Is.EqualTo("JRWox-i6aAk"));
	}

	[TestCase("")]
	[TestCase("not a url")]
	[TestCase("https://example.com/watch?v=JRWox-i6aAk")]
	[TestCase("https://youtube.com.example.com/watch?v=JRWox-i6aAk")]
	[TestCase("https://youtube.com@evil.example/watch?v=JRWox-i6aAk")]
	[TestCase("file:///watch?v=JRWox-i6aAk")]
	[TestCase("https://youtube.com:1234/watch?v=JRWox-i6aAk")]
	[TestCase("https://youtube.com/watch?v=")]
	[TestCase("https://youtube.com/watch?v=short")]
	[TestCase("https://youtube.com/watch?v=JRWox-i6aAk&v=other")]
	[TestCase("https://youtube.com/watch?v=../outside")]
	[TestCase("https://youtube.com/watch?v=%2e%2e%2foutside")]
	[TestCase("https://youtu.be/JRWox-i6aAk/extra")]
	[TestCase("https://youtube.com/playlist?list=playlist")]
	public void GetVideoIdFromUrl_RejectsUnsupportedOrUnsafeInput(string url) {
		Assert.That(new UrlFilter().GetVideoIdFromUrl(url), Is.Empty);
	}
}
