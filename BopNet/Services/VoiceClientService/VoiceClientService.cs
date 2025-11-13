using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Gateway.Voice;

namespace BopNet.Services.VoiceClientService;

public class VoiceClientService(ILogger<Interactions> logger) : IVoiceClientService
{
    private readonly ConcurrentDictionary<ulong, VoiceClient> _voiceClients = new();
    private readonly ConcurrentDictionary<ulong, bool> _paused = new();

    /// <summary>
    /// Returns a voiceClient for the specific guild.
    /// </summary>
    /// <param name="client">Client which the voice channel will be made for</param>
    /// <param name="guild">Guild Id of the server which the bot is in</param>
    /// <param name="voiceState"></param>
    /// <returns>New or existing VoiceClient for guild.</returns>
    public async Task<VoiceClient?> StartVoiceClient(GatewayClient client, ulong guild, VoiceState voiceState)
    {
        try
        {
            if (_voiceClients.TryGetValue(guild, out var voiceClient)) return voiceClient;
            voiceClient = await client.JoinVoiceChannelAsync(
                guild,
                voiceState.ChannelId.GetValueOrDefault());
            _voiceClients.TryAdd(guild, voiceClient);
            return voiceClient;
        }
        catch (Exception e)
        {
            Console.WriteLine("Could not start voice client: {0}", e.Message);
            return null;
        }
    }

    public async Task StopStream(GatewayClient client, ulong guildId)
    {
        try
        {
            if (_voiceClients.TryGetValue(guildId, out var voiceClient)) await voiceClient.CloseAsync();
            _voiceClients.TryRemove(guildId, out _);
            var voiceState = new VoiceStateProperties(guildId, null);
            await client.UpdateVoiceStateAsync(voiceState);
            logger.LogInformation("Voice client stopped");
        }
        catch (Exception e)
        {
            logger.LogError("Failed to stop voice client: " + e);
        }
    }

    public VoiceClient? GetVoiceClientService(ulong guildId) => _voiceClients.GetValueOrDefault(guildId);
    public bool GuildHasVoiceClientService(ulong guildId) => _voiceClients.TryGetValue(guildId, out _);
    public void PauseStream(ulong guildId) => _paused[guildId] = true;
    public void ResumeStream(ulong guildId) => _paused[guildId] = false;
    public bool IsPaused(ulong guildId) => _paused.TryGetValue(guildId, out var paused) && paused;
}