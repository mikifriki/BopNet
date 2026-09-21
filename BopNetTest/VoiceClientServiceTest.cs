using BopNet;
using BopNet.Services.VoiceClientService;
using Microsoft.Extensions.Logging.Abstractions;
using NetCord.Gateway.Voice;

namespace BopNetTest;

public class VoiceClientServiceTest {
	[Test]
	public async Task StopStream_WhenCloseFails_RemovesCachedClientAndAllowsReplacement() {
		const ulong guildId = 42;
		var service = new VoiceClientService(NullLogger<Interactions>.Instance);
		var guildClient = service.GetGuildVoiceClient(guildId);
		using var cancellation = new CancellationTokenSource();
		guildClient.Cancellation = cancellation;
		using var failedClient = new VoiceClient(1, "session", "example.invalid", guildId, 2, "unused");
		guildClient.VoiceClient = failedClient;
		service.PauseStream(guildId);

		// An unstarted client throws on CloseAsync, reproducing cleanup after a
		// failed connection without connecting to Discord or loading native code.
		Assert.ThrowsAsync<InvalidOperationException>(async () => await failedClient.CloseAsync());
		await service.StopStream(null!, guildId);

		Assert.Multiple(() => {
			Assert.That(service.GuildHasVoiceClientService(guildId), Is.False);
			Assert.That(service.IsPaused(guildId), Is.False);
			Assert.That(cancellation.IsCancellationRequested, Is.True);
			Assert.That(guildClient.Cancellation, Is.Null);
			Assert.That(service.GetGuildVoiceClient(guildId), Is.SameAs(guildClient));
		});

		using var replacement = new VoiceClient(1, "retry", "example.invalid", guildId, 2, "unused");
		guildClient.VoiceClient = replacement;
		Assert.That(await service.StartVoiceClient(null!, guildId, null!), Is.SameAs(replacement));
	}
}
