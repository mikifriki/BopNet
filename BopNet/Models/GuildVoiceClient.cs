using NetCord.Gateway.Voice;

namespace BopNet.Models;

public class GuildVoiceClient {
	public VoiceClient? VoiceClient { get; set; }
	public CancellationTokenSource? Cancellation { get; set; }
	public SemaphoreSlim Gate { get; } = new(1, 1);
}
