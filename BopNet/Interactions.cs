using BopNet.Services;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace BopNet;

public class Interactions(
    IAudioService audioService,
    IVoiceClientService voiceClientService,
    IMusicQueueService musicQueueService) : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly CancellationTokenSource _cancelToken = new();

    [SlashCommand("play", "Plays music", Contexts = [InteractionContextType.Guild])]
    public async Task PlayAsync(string track)
    {
        var client = Context.Client;
        var guildId = GetGuildId(Context.Guild);
        if (guildId == 0) return;
        var guild = Context.Guild!;

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

        var voiceClient = await voiceClientService.StartVoiceClient(client, guildId, voiceState);

        if (voiceClient == null)
        {
            await RespondAsync(InteractionCallback.Message("Failed to initialize the voice client."));
            return;
        }

        await voiceClient.StartAsync();
        await voiceClient.EnterSpeakingStateAsync(SpeakingFlags.Microphone);
        var outStream = voiceClient.CreateOutputStream();

        musicQueueService.AddMusicQueue(guildId, track);
        await RespondAsync(InteractionCallback.Message($"Added {track} to queue"));

        OpusEncodeStream stream = new(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

        while (musicQueueService.HasNextTrack(guildId))
        {
            var song = musicQueueService.GetNextTrack(guildId);
            if (song is null) break;

            await audioService.StartAudio(guildId, song, _cancelToken.Token);
            await Task.Delay(500);
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
    /// <returns>GuildID for Guild or 000 if none is found</returns>
    private static ulong GetGuildId(Guild? guildId) => guildId?.Id ?? 000;

    private void DisconnectBot(ulong guildId)
    {
        voiceClientService.StopStream(Context.Client, guildId);
        audioService.StopAudio(guildId);
        musicQueueService.ClearMusicQueue(guildId);
    }
}