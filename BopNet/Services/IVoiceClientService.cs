using NetCord.Gateway;
using NetCord.Gateway.Voice;

namespace BopNet.Services;

public interface IVoiceClientService
{
    Task<VoiceClient?> StartVoiceClient(GatewayClient client,  ulong guild, VoiceState voiceState);
    public VoiceClient? GetVoiceClientService(ulong guildId);
    public bool GuildHasVoiceClientService(ulong guildId);
    public void PauseStream(ulong guildId);
    public void ResumeStream(ulong guildId);
    public void StopStream(GatewayClient client, ulong guildId);
    public bool IsPaused(ulong guildId);
}