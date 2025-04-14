using NetCord.Gateway;
using NetCord.Gateway.Voice;

namespace BopNet.Services;

public class VoiceClientService : IVoiceClientService
{
    private VoiceClient? _voiceClient;
    private readonly Dictionary<ulong, bool> _paused = new();

    /// <summary>
    /// Returns a voiceClient for the specific guild.
    /// </summary>
    /// <param name="client"></param>
    /// <param name="guild"></param>
    /// <param name="voiceState"></param>
    /// <returns>New or existing VoiceClient for guild.</returns>
    public async Task<VoiceClient?> StartVoiceClient(GatewayClient client, ulong guild, VoiceState voiceState)
    {
        try
        {
            if (_voiceClient != null) return _voiceClient;
            _voiceClient = await client.JoinVoiceChannelAsync(
                guild,
                voiceState!.ChannelId.GetValueOrDefault());

            return _voiceClient;
        }
        catch (Exception e)
        {
            Console.WriteLine("Could not start voice client: {0}", e.Message);
            return null;
        }
    }

    public VoiceClient? GetVoiceClientService() => _voiceClient;
    public bool GuildHasVoiceClientService(ulong guildId) => _voiceClient != null;
    public void PauseStream(ulong guildId) => _paused[guildId] = true;
    public void ResumeStream(ulong guildId) => _paused[guildId] = false;

    public void StopStream(GatewayClient client, ulong guildId)
    {
        try
        {
            if (_voiceClient == null) return;
            _voiceClient!.CloseAsync();
            _voiceClient = null;
            client.CloseAsync();
            client.StartAsync();
        }
        catch (Exception e) {
            // By this point voiceclient does not exist
        }
    }

    public bool IsPaused(ulong guildId) => _paused.TryGetValue(guildId, out var paused) && paused;
}