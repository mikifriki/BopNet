using System.Collections.Concurrent;
using BopNet.Models;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Gateway.Voice;

namespace BopNet.Services.VoiceClientService;

public class VoiceClientService(ILogger<Interactions> logger) : IVoiceClientService {
	private readonly ConcurrentDictionary<ulong, GuildVoiceClient> _voiceClients = new();
	private readonly ConcurrentDictionary<ulong, bool> _paused = new();

	/// <summary>
	/// Returns a voiceClient for the specific guild.
	/// </summary>
	/// <param name="client">Client which the voice channel will be made for</param>
	/// <param name="guild">Guild Id of the server which the bot is in</param>
	/// <param name="voiceState"></param>
	/// <returns>New or existing VoiceClient for guild.</returns>
	public async Task<VoiceClient?> StartVoiceClient(GatewayClient client, ulong guild, VoiceState voiceState) {
		try{
			var guildClient = GetGuildVoiceClient(guild);
			if (guildClient.VoiceClient is {} existing) return existing;
			var voiceClient = await client.JoinVoiceChannelAsync(
			guild,
			voiceState.ChannelId.GetValueOrDefault());
			guildClient.VoiceClient = voiceClient;
			return voiceClient;
		}
		catch (Exception e){
			Console.WriteLine("Could not start voice client: {0}", e.Message);
			return null;
		}
	}

	public async Task StopStream(GatewayClient client, ulong guildId) {
		var guildClient = GetGuildVoiceClient(guildId);
		var voiceClient = guildClient.VoiceClient;
		guildClient.VoiceClient = null;
		if (guildClient.Cancellation is { } cancellation)
			await cancellation.CancelAsync();
		guildClient.Cancellation = null;
		_paused.TryRemove(guildId, out _);
		try{
			if (voiceClient is not null) await voiceClient.CloseAsync();
		}
		catch (Exception e){
			logger.LogError(e, "Failed to close voice client for guild {GuildId}", guildId);
		}
		finally{
			voiceClient?.Dispose();
		}

		try{
			var voiceState = new VoiceStateProperties(guildId, null);
			await client.UpdateVoiceStateAsync(voiceState);
			logger.LogInformation("Voice client stopped");
		}
		catch (Exception e){
			logger.LogError(e, "Failed to leave voice channel for guild {GuildId}", guildId);
		}
	}

	public GuildVoiceClient GetGuildVoiceClient(ulong guildId) => _voiceClients.GetOrAdd(guildId, _ => new GuildVoiceClient());
	public VoiceClient? GetVoiceClientService(ulong guildId) => GetGuildVoiceClient(guildId).VoiceClient;
	public bool GuildHasVoiceClientService(ulong guildId) => GetVoiceClientService(guildId) is not null;
	public void PauseStream(ulong guildId) => _paused[guildId] = true;
	public void ResumeStream(ulong guildId) => _paused[guildId] = false;
	public bool IsPaused(ulong guildId) => _paused.TryGetValue(guildId, out var paused) && paused;
}
