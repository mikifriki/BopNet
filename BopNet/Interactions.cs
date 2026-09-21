using BopNet.Services.AudioService;
using BopNet.Services.DataBaseService;
using BopNet.Services.MusicQueueService;
using BopNet.Services.TrackCacheService;
using BopNet.Services.VoiceClientService;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace BopNet;

using Helpers;
using Microsoft.Extensions.Logging;
using Models;

public class Interactions(
	ILogger<Interactions> logger,
	IAudioService audioService,
	IVoiceClientService voiceClientService,
	IMusicQueueService musicQueueService,
	IDatabase database,
	ITrackCacheService trackCacheService) : ApplicationCommandModule<ApplicationCommandContext> {
	private readonly UrlFilter _urlFilter = new();

	[SlashCommand("play", "Plays music", Contexts = [InteractionContextType.Guild])]
	public async Task PlayAsync(string track) {
		var guildId = GetGuildId(Context.Guild);
		var guild = Context.Guild;

		logger.LogInformation("Started playing track");
		if (guild is null || guildId == 0){
			await RespondAsync(InteractionCallback.Message("Could not find Guild."));
			return;
		}

		var videoId = _urlFilter.GetVideoIdFromUrl(track);
		if (videoId.Length == 0){
			await RespondAsync(InteractionCallback.Message("Please provide a valid YouTube video URL."));
			return;
		}
		track = $"https://www.youtube.com/watch?v={videoId}";

		if (!guild.VoiceStates.TryGetValue(Context.User.Id, out var voiceState)){
			await RespondAsync(InteractionCallback.Message("You are not connected to any voice channel!"));
			return;
		}

		await RespondAsync(InteractionCallback.DeferredMessage());
		var guildClient = voiceClientService.GetGuildVoiceClient(guildId);
		using var cancellation = new CancellationTokenSource();
		var ownsPlayback = false;
		try{
			VoiceClient? voiceClient;
			await guildClient.Gate.WaitAsync(cancellation.Token);
			try{
				if (guildClient.Cancellation is not null){
					musicQueueService.AddMusicQueue(guildId, track);
					await Context.Interaction.ModifyResponseAsync(message => message.Content = $"Added {track} to queue");
					return;
				}

				guildClient.Cancellation = cancellation;
				ownsPlayback = true;
				voiceClient = await voiceClientService.StartVoiceClient(Context.Client, guildId, voiceState);
				if (voiceClient is null){
					await Context.Interaction.ModifyResponseAsync(message => message.Content = "Failed to start the voice client.");
					return;
				}

				await voiceClient.StartAsync(cancellation.Token);
				await voiceClient.EnterSpeakingStateAsync(new SpeakingProperties(SpeakingFlags.Microphone), cancellationToken: cancellation.Token);
				musicQueueService.AddMusicQueue(guildId, track);
				await Context.Interaction.ModifyResponseAsync(message => message.Content = $"Added {track} to queue");
			}
			finally{
				guildClient.Gate.Release();
			}

			await using var voiceStream = voiceClient.CreateVoiceStream();
			await using OpusEncodeStream stream = new(
			voiceStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio
			);

			while (!cancellation.IsCancellationRequested){
				Task streaming;
				await guildClient.Gate.WaitAsync(cancellation.Token);
				try{
					cancellation.Token.ThrowIfCancellationRequested();
					var nextSong = musicQueueService.GetNextTrack(guildId);
					if (nextSong is null){
						// Enqueue and the final disconnect must not race.
						await stream.FlushAsync(cancellation.Token);
						await DisconnectBot(guildId);
						return;
					}

					var song = UpdateTrackPlayCount(nextSong) ?? SaveNewTrack(nextSong);
					if (song is null) break;
					song.FullUrl = nextSong;
					if (trackCacheService.IsTrackCached(song))
						await audioService.StartCachedAudio(guildId, song, cancellation.Token);
					else
						await audioService.StartAudio(guildId, song, cancellation.Token);

					// Capture this track's audio before another command can stop it.
					streaming = audioService.StreamToDiscordAsync(stream, guildId, cancellation.Token);
				}
				finally{
					guildClient.Gate.Release();
				}

				await streaming;
			}
		}
		catch (Exception e) when (cancellation.IsCancellationRequested){
			logger.LogDebug(e, "Playback cancelled for guild {GuildId}", guildId);
		}
		finally{
			if (ownsPlayback)
				await FinishPlayback(guildId, cancellation);
		}
	}

	[SlashCommand("skip", "Skip the song", Contexts = [InteractionContextType.Guild])]
	public Task SkipAsync() => ControlAudio(audioService.StopAudio, "Song skipped!");

	[SlashCommand("stop", "Stop the music", Contexts = [InteractionContextType.Guild])]
	public async Task StopAsync() {
		var guildId = GetGuildId(Context.Guild);
		if (guildId == 0) return;
		await RespondAsync(InteractionCallback.DeferredMessage());
		await WithGuildLock(guildId, () => DisconnectBot(guildId));
		await Context.Interaction.ModifyResponseAsync(message => message.Content = "Music stopped!");
	}

	[SlashCommand("pause", "Pause the music", Contexts = [InteractionContextType.Guild])]
	public Task PauseAsync() => ControlAudio(audioService.PauseAudio, "Music Paused!");

	[SlashCommand("resume", "resume the music", Contexts = [InteractionContextType.Guild])]
	public Task ResumeAsync() => ControlAudio(audioService.ResumeAudio, "Music Resumed!");

	private Task ControlAudio(Action<ulong> action, string response) => ControlAudio(guildId => {
		action(guildId);
		return Task.CompletedTask;
	}, response);

	private async Task ControlAudio(Func<ulong, Task> action, string response) {
		var guildId = GetGuildId(Context.Guild);
		if (guildId == 0) return;
		await RespondAsync(InteractionCallback.DeferredMessage());
		await WithGuildLock(guildId, () => action(guildId));
		await Context.Interaction.ModifyResponseAsync(message => message.Content = response);
	}

	/// <summary>
	/// Gets GuildID if possible.
	/// </summary>
	/// <param name="guildId"></param>
	/// <returns>GuildID for Guild or 0 if none is found</returns>
	private static ulong GetGuildId(Guild? guildId) => guildId?.Id ?? 0;

	private async Task DisconnectBot(ulong guildId) {
		voiceClientService.GetGuildVoiceClient(guildId).Cancellation?.Cancel();
		try{
			await audioService.StopAudio(guildId);
		}
		finally{
			musicQueueService.ClearMusicQueue(guildId);
			await voiceClientService.StopStream(Context.Client, guildId);
		}
	}

	private Task FinishPlayback(ulong guildId, CancellationTokenSource cancellation) => WithGuildLock(guildId, async () => {
		// A stopped command must not clean up a newer playback session.
		if (ReferenceEquals(voiceClientService.GetGuildVoiceClient(guildId).Cancellation, cancellation))
			await DisconnectBot(guildId);
	});

	private async Task WithGuildLock(ulong guildId, Func<Task> action) {
		var gate = voiceClientService.GetGuildVoiceClient(guildId).Gate;
		await gate.WaitAsync();
		try{
			await action();
		}
		finally{
			gate.Release();
		}
	}

	private Track? UpdateTrackPlayCount(string trackUrl) {
		var videoId = _urlFilter.GetVideoIdFromUrl(trackUrl);
		var existingTrack = database.GetTrack(videoId);

		if (existingTrack is null) return null;

		database.UpdateTrackPlayCount(existingTrack);
		logger.LogInformation("Updated track " + existingTrack.Reference);
		return existingTrack;
	}

	private Track? SaveNewTrack(string trackUrl) {
		var videoId = _urlFilter.GetVideoIdFromUrl(trackUrl);
		Track? savedTrack = null;
		try{
			var newTrack = new Track {
				Reference = videoId,
				FullUrl = trackUrl,
				FilePath = trackCacheService.GetCachedTrackPath(videoId)
			};

			savedTrack = database.SaveTrack(newTrack);
		}
		catch (InvalidOperationException e){
			logger.LogError(e.Message);
		}

		return savedTrack;
	}
}
