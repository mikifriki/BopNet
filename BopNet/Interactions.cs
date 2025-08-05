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
            var song = musicQueueService.GetNextTrack(guildId);
            if (song is null) break;
            AddTrackToDb(song);

            await audioService.StartAudio(guildId, song, _cancelToken.Token);
            await Task.Delay(1000);
            await audioService.StreamToDiscordAsync(stream, guildId, _cancelToken.Token);
        }

        await stream.FlushAsync();

        DisconnectBot(guildId);
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
        DisconnectBot(guildId);

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

    private void DisconnectBot(ulong guildId)
    {
        voiceClientService.StopStream(Context.Client, guildId);
        audioService.StopAudio(guildId);
        musicQueueService.ClearMusicQueue(guildId);
    }
    
    // This is generally not required and is only useful if reusing existing bot to clear commands. As such this is private and not shown and uses hardcoded applicationid
    private async Task ClearCommandsHandler() {
        var context = Context;
        if (context.Guild is not null)
        {
            var guildCommands = await context.Client.Rest.GetGuildApplicationCommandsAsync(682915212814581791, context.Guild.Id);
            foreach (var command in guildCommands)
                await context.Client.Rest.DeleteGuildApplicationCommandAsync(682915212814581791, context.Guild.Id, command.Id);

            await RespondAsync(InteractionCallback.Message("Could not find Guild."));
        }
        else
        {
            var globalCommands = await context.Client.Rest.GetGlobalApplicationCommandsAsync(682915212814581791);
            foreach (var command in globalCommands)
                await context.Client.Rest.DeleteGlobalApplicationCommandAsync(682915212814581791, command.Id);

        }
    }

    private void AddTrackToDb(string trackUrl) {
        var videoId  = _urlFilter.GetVideoIdFromUrl(trackUrl);
        var newTrack = new Track {
            Reference = videoId
        };
        
        logger.LogInformation("Adding track to db " + videoId);
        database.SaveTrack(newTrack);
    }
}