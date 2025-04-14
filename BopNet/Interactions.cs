using System.Diagnostics;
using System.Runtime.InteropServices;
using BopNet.Services;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace BopNet;

public class Interactions(
    IFFmpegService fFmpegService,
    IVoiceClientService voiceClientService,
    IMusicQueueService musicQueueService) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("play", "Plays music", Contexts = [InteractionContextType.Guild])]
    public async Task PlayAsync(string track)
    {
        var client = Context.Client;
        var guildId = GetGuildId(Context.Guild);
        if (guildId == 0) return;
        var guild = Context.Guild!;
        var audioUrl = GetYouTubeAudioStreamUrl(track);
        musicQueueService.AddMusicQueue(guildId, audioUrl);

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
            await RespondAsync(InteractionCallback.Message("Track added to queue"));
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

        OpusEncodeStream stream = new(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

        // 20ms of PCM (48kHz stereo s16le)
        var buffer = new byte[3840];
        try
        {
            await PlayQueueAsync(guildId, buffer, stream);
        }
        catch (Exception)
        {
            return;
        }

        await stream.FlushAsync();
        // Disconnect the bot once the playback has ended.
        DisconnectBot(guildId);
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
        voiceClientService.PauseStream(guildId);

        await RespondAsync(InteractionCallback.Message("Music Paused!"));
    }

    [SlashCommand("resume", "resume the music", Contexts = [InteractionContextType.Guild])]
    public async Task ResumeAsync()
    {
        var guildId = GetGuildId(Context.Guild);
        if (guildId == 0) return;
        voiceClientService.ResumeStream(guildId);

        await RespondAsync(InteractionCallback.Message("Music Resumed!"));
    }

    private string GetYouTubeAudioStreamUrl(string videoUrl)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"-f bestaudio -g \"{videoUrl}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return output;
            }
        }
        catch (Exception ex)
        {
            throw new ExternalException("Failed to start YT stream: " + ex.Message);
        }

        return string.Empty;
    }

    /// <summary>
    /// Plays music from the created queue
    /// </summary>
    /// <param name="guildId"></param>
    /// <param name="buffer"></param>
    /// <param name="stream"></param>
    private async Task PlayQueueAsync(ulong guildId, byte[] buffer, OpusEncodeStream stream)
    {
        while (true)
        {
            var track = musicQueueService.GetNextTrack(guildId);
            if (track == null)
            {
                Console.WriteLine("No more music tracks available.");
                break;
            }

            try
            {
                var ffmpeg = fFmpegService.StartFFmpeg(guildId, track);
                await HandleAudioStreaming(ffmpeg!, buffer, stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while playing track: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Streams audio to Discord
    /// </summary>
    /// <param name="ffmpeg"></param>
    /// <param name="buffer"></param>
    /// <param name="stream"></param>
    /// <exception cref="ExternalException"></exception>
    private async Task HandleAudioStreaming(Process ffmpeg, byte[] buffer, OpusEncodeStream stream)
    {
        var guildId = GetGuildId(Context.Guild);
        if (guildId == 0) return;
        try
        {
            int bytesRead;
            while ((bytesRead = await ffmpeg.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) >
                   0)
            {
                // Polling should be reworked at some point but this is fine for now
                if (voiceClientService.IsPaused(guildId))
                {
                    await Task.Delay(100);
                    continue;
                }

                if (bytesRead == 0)
                    break;

                await stream.WriteAsync(buffer.AsMemory(0, bytesRead));
            }
        }
        catch (InvalidOperationException e)
        {
            // If caught here then the FFMPEG process was closed during the playback
            throw new ExternalException("Failed to start audio streaming: " + e.Message);
        }
    }

    /// <summary>
    /// Gets GuildID if possible.
    /// </summary>
    /// <param name="guildId"></param>
    /// <returns>GuildID for Guild or ++</returns>
    private ulong GetGuildId(Guild? guildId) => guildId?.Id ?? 000;

    private void DisconnectBot(ulong guildId)
    {
        voiceClientService.StopStream(Context.Client, guildId);
        fFmpegService.StopFFmpeg(guildId);
    }
}