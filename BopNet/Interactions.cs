using BopNet.Services;
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
    IDatabase database) : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly CancellationTokenSource _cancelToken = new();
    private readonly UrlFilter _urlFilter = new();

    [SlashCommand("play", "Plays music", Contexts = [InteractionContextType.Guild])]
    public async Task PlayAsync(string track)
    {
        var guildId = GetGuildId(Context.Guild);
        var guild = Context.Guild;

        logger.LogInformation("Started playing track");
        if (guild is null || guildId == 0)
        {
            await RespondAsync(InteractionCallback.Message("Could not find Guild."));
            return;
        }
        
        if (!Uri.IsWellFormedUriString(track, UriKind.Absolute))
        {
            await RespondAsync(InteractionCallback.Message("Invalid track!"));
            return;
        }

        if (!guild.VoiceStates.TryGetValue(Context.User.Id, out var voiceState))
        {
            await RespondAsync(InteractionCallback.Message("You are not connected to any voice channel!"));
            return;
        }

        if (voiceClientService.GuildHasVoiceClientService(guildId))
        {
            musicQueueService.AddMusicQueue(guildId, track);
            await RespondAsync(InteractionCallback.Message($"Added {track} to queue"));
            return;
        }

        var voiceClient = await voiceClientService.StartVoiceClient(Context.Client, guildId, voiceState);

        if (voiceClient is null)
        {
            await RespondAsync(InteractionCallback.Message("Failed to start the voice client."));
            return;
        }

        await voiceClient.StartAsync();
        await voiceClient.EnterSpeakingStateAsync(SpeakingFlags.Microphone);

        musicQueueService.AddMusicQueue(guildId, track);
        await RespondAsync(InteractionCallback.Message($"Added {track} to queue"));

        OpusEncodeStream stream = new(
            voiceClient.CreateOutputStream(), PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio
        );

        while (musicQueueService.HasNextTrack(guildId))
        {
            var nextSong = musicQueueService.GetNextTrack(guildId);
            if (nextSong is null) break;
            var song = AddTrackToDb(nextSong);

            await audioService.StartAudio(guildId, song, _cancelToken.Token);
            await Task.Delay(1000);
            await audioService.StreamToDiscordAsync(stream, guildId, _cancelToken.Token);
        }

        await stream.FlushAsync();
        
        await DisconnectBot(guildId);
    }

    [SlashCommand("skip", "Skip the song", Contexts = [InteractionContextType.Guild])]
    public async Task SkipAsync()
    {
        var guildId = GetGuildId(Context.Guild);
        if (guildId == 0) return;
        audioService.StopAudio(guildId);

        await RespondAsync(InteractionCallback.Message("Song skipped!"));
    }

    [SlashCommand("stop", "Stop the music", Contexts = [InteractionContextType.Guild])]
    public async Task StopAsync()
    {
        var guildId = GetGuildId(Context.Guild);
        if (guildId == 0) return;
        await DisconnectBot(guildId);

        await RespondAsync(InteractionCallback.Message("Music stopped!"));
    }

    [SlashCommand("pause", "Pause the music", Contexts = [InteractionContextType.Guild])]
    public async Task PauseAsync()
    {
        var guildId = GetGuildId(Context.Guild);
        if (guildId == 0) return;
        audioService.PauseAudio(guildId);
        await RespondAsync(InteractionCallback.Message("Music Paused!"));
    }

    [SlashCommand("resume", "resume the music", Contexts = [InteractionContextType.Guild])]
    public async Task ResumeAsync()
    {
        var guildId = GetGuildId(Context.Guild);
        if (guildId == 0) return;
        audioService.ResumeAudio(guildId);

        await RespondAsync(InteractionCallback.Message("Music Resumed!"));
    }

    /// <summary>
    /// Gets GuildID if possible.
    /// </summary>
    /// <param name="guildId"></param>
    /// <returns>GuildID for Guild or 0 if none is found</returns>
    private static ulong GetGuildId(Guild? guildId) => guildId?.Id ?? 0;

    private async Task DisconnectBot(ulong guildId)
    {
        await voiceClientService.StopStream(Context.Client, guildId);
        audioService.StopAudio(guildId);
        musicQueueService.ClearMusicQueue(guildId);
    }

    private Track AddTrackToDb(string trackUrl) {
        var videoId  = _urlFilter.GetVideoIdFromUrl(trackUrl);
        var newTrack = new Track {
            Reference = videoId,
            FullUrl = trackUrl
        };
        
        logger.LogInformation("Adding track to db " + videoId);
        database.SaveTrack(newTrack);
        return newTrack;
    }
}