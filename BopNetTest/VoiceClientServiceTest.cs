using System.Collections.Concurrent;
using System.Reflection;
using BopNet;
using BopNet.Services.VoiceClientService;
using Microsoft.Extensions.Logging.Abstractions;
using NetCord.Gateway.Voice;

namespace BopNetTest;

public class VoiceClientServiceTest
{
    [Test]
    public async Task StopStream_WhenCloseFails_RemovesCachedClientAndAllowsReplacement()
    {
        const ulong guildId = 42;
        var service = new VoiceClientService(NullLogger<Interactions>.Instance);
        var clients = (ConcurrentDictionary<ulong, VoiceClient>)typeof(VoiceClientService)
            .GetField("_voiceClients", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;
        using var failedClient = new VoiceClient(1, "session", "example.invalid", guildId, 2, "unused");
        clients[guildId] = failedClient;
        service.PauseStream(guildId);

        // An unstarted client throws on CloseAsync, reproducing cleanup after a
        // failed connection without connecting to Discord or loading native code.
        Assert.ThrowsAsync<InvalidOperationException>(async () => await failedClient.CloseAsync());
        await service.StopStream(null!, guildId);

        Assert.Multiple(() =>
        {
            Assert.That(service.GuildHasVoiceClientService(guildId), Is.False);
            Assert.That(service.IsPaused(guildId), Is.False);
        });

        using var replacement = new VoiceClient(1, "retry", "example.invalid", guildId, 2, "unused");
        Assert.That(clients.TryAdd(guildId, replacement), Is.True);
        Assert.That(await service.StartVoiceClient(null!, guildId, null!), Is.SameAs(replacement));
    }
}
