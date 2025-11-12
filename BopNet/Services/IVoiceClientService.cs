using NetCord.Gateway;
using NetCord.Gateway.Voice;

namespace BopNet.Services;

public interface IVoiceClientService
{
    public Task<VoiceClient?> StartVoiceClient(GatewayClient client,  ulong guild, VoiceState voiceState);
    
    public Task StopStream(GatewayClient client, ulong guildId);
    public VoiceClient? GetVoiceClientService(ulong guildId);
    public bool GuildHasVoiceClientService(ulong guildId);
    public void PauseStream(ulong guildId);
    public void ResumeStream(ulong guildId);
    public bool IsPaused(ulong guildId);
}