using NetCord.Gateway;
using NetCord.Gateway.Voice;

namespace BopNet.Services;

public class VoiceClientService : IVoiceClientService
{
    private readonly Dictionary<ulong, VoiceClient> _voiceClients = new();
    private readonly Dictionary<ulong, bool> _paused = new();

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
                voiceState!.ChannelId.GetValueOrDefault());

            return voiceClient;
        }
        catch (Exception e)
        {
            Console.WriteLine("Could not start voice client: {0}", e.Message);
            return null;
        }
    }

    public VoiceClient? GetVoiceClientService(ulong guildId) => _voiceClients.GetValueOrDefault(guildId);
    public bool GuildHasVoiceClientService(ulong guildId) => _voiceClients.TryGetValue(guildId, out var voiceClient);
    public void PauseStream(ulong guildId) => _paused[guildId] = true;
    public void ResumeStream(ulong guildId) => _paused[guildId] = false;

    public void StopStream(GatewayClient client, ulong guildId)
    {
        try
        {
            if (_voiceClients.TryGetValue(guildId, out var voiceClient)) return;
            voiceClient!.CloseAsync();
            _voiceClients.Remove(guildId);
            client.CloseAsync();
            client.StartAsync();
        }
        catch (Exception e)
        {
            // By this point voiceclient does not exist
        }
    }

    public bool IsPaused(ulong guildId) => _paused.TryGetValue(guildId, out var paused) && paused;
}