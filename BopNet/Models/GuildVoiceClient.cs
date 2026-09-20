using NetCord.Gateway;
using NetCord.Gateway.Voice;

namespace BopNet.Models;

public class GuildVoiceClient
{
    public VoiceClient? VoiceClient { get; set; }
    public OpusEncodeStream? Stream { get; set; }
    
}