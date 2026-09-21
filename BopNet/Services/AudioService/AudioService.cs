using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using BopNet.Models;
using BopNet.Services.TrackCacheService;

namespace BopNet.Services.AudioService;

public class AudioService(ITrackCacheService trackCacheService) : IAudioService {
	private readonly ConcurrentDictionary<ulong, GuildAudio> _ffmpegProcesses = new();

	/// <summary>
	/// Starts streaming given url to ffmpeg buffer
	/// </summary>
	/// <param name="guildId">Discord guild ID</param>
	/// <param name="track">Music track which should be read</param>
	/// <param name="token">Cancellation token</param>
	public async Task StartAudio(ulong guildId, Track track, CancellationToken token) {
		token.ThrowIfCancellationRequested();
		await StopAudio(guildId);
		token.ThrowIfCancellationRequested();
		var ffmpeg = new Process {
			StartInfo = new ProcessStartInfo {
				FileName = "ffmpeg",
				Arguments = "-progress pipe:2 -nostats -i pipe:0 -f s16le -vn -ar 48000 -ac 2 pipe:1",
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			}
		};

		var ytDlpProcess = new Process {
			StartInfo = new ProcessStartInfo {
				FileName = "yt-dlp",
				ArgumentList = { "--no-playlist", "-o", "-", "-f", "bestaudio", "--no-part", "--", track.FullUrl },
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true
			}
		};

		var audioProcess = new GuildAudio { Ffmpeg = ffmpeg, Ytdl = ytDlpProcess };

		// Progress shares stderr with diagnostics; keep draining both during playback.
		ffmpeg.ErrorDataReceived += (_, e) => {
			if (e.Data is null || !e.Data.StartsWith("out_time="))
				return;

			audioProcess.TimeStamp = e.Data["out_time=".Length..].Trim();
		};

		_ffmpegProcesses[guildId] = audioProcess;
		try{
			var downloadPath = trackCacheService.GetDownloadCachePath(track);
			var cachedPath = trackCacheService.GetCachedTrackPath(track);
			ffmpeg.Start();
			ffmpeg.BeginErrorReadLine();
			ytDlpProcess.Start();
			audioProcess.PipingTask = PipeAsync(ytDlpProcess.StandardOutput.BaseStream, ffmpeg.StandardInput.BaseStream,
			downloadPath, cachedPath, audioProcess, token);
		}
		catch{
			await StopAudio(guildId);
			throw;
		}
	}

	public async Task StartCachedAudio(ulong guildId, Track track, CancellationToken token) {
		token.ThrowIfCancellationRequested();
		await StopAudio(guildId);
		token.ThrowIfCancellationRequested();
		var ffmpeg = new Process {
			StartInfo = new ProcessStartInfo {
				FileName = "ffmpeg",
				ArgumentList = {
					"-progress", "pipe:2", "-nostats", "-i", trackCacheService.GetCachedTrackPath(track),
					"-f", "s16le", "-ar", "48000", "-ac", "2", "pipe:1"
				},
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			}
		};

		var audioProcess = new GuildAudio { Ffmpeg = ffmpeg };

		// Progress shares stderr with diagnostics; keep draining both during playback.
		ffmpeg.ErrorDataReceived += (_, e) => {
			if (e.Data is null || !e.Data.StartsWith("out_time="))
				return;

			audioProcess.TimeStamp = e.Data["out_time=".Length..].Trim();
		};

		_ffmpegProcesses[guildId] = audioProcess;
		try{
			ffmpeg.Start();
			ffmpeg.BeginErrorReadLine();
		}
		catch{
			await StopAudio(guildId);
			throw;
		}
	}

	/// <summary>
	/// Streams the previously started stream into Discord.
	/// </summary>
	/// <param name="discordOut">Discord Stream which awaits input</param>
	/// <param name="guildId">Discord Guild Id</param>
	/// <param name="token">Cancellation token</param>
	public Task StreamToDiscordAsync(Stream discordOut, ulong guildId, CancellationToken token) {
		if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)) return Task.CompletedTask;
		return audio.StreamingTask = StreamAsync(discordOut, audio, token);
	}

	private async static Task StreamAsync(Stream discordOut, GuildAudio audio, CancellationToken token) {
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, audio.Cancellation.Token);
		var streamingToken = cancellation.Token;
		try{
			var baseStream = audio.Ffmpeg?.StandardOutput.BaseStream;
			if (baseStream is null) return;
			var buffer = new byte[GuildAudio.BufferSize];
			var silence = new byte[GuildAudio.BufferSize];

			while (!streamingToken.IsCancellationRequested){
				var paused = audio.Paused;
				var data = paused ? silence : buffer;
				var bytesToWrite = data.Length;
				if (!paused){
					var bytesRead = await baseStream.ReadAsync(buffer, streamingToken);
					if (bytesRead == 0) break;
					bytesToWrite = bytesRead;
				}

				await discordOut.WriteAsync(data.AsMemory(0, bytesToWrite), streamingToken);
			}
		}
		catch (OperationCanceledException) when (streamingToken.IsCancellationRequested){
			// Skipping ends this track normally, allowing the playback loop to advance.
		}
		catch (IOException) when (streamingToken.IsCancellationRequested){
			// Stopping the process may close its pipe before cancellation is observed.
		}
		finally{
			await audio.Cancellation.CancelAsync();
			await audio.PipingTask;
		}
	}

	private async static Task PipeAsync(Stream input, Stream output, string path, string finalPath, GuildAudio audio,
		CancellationToken token) {
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, audio.Cancellation.Token);
		var pipingToken = cancellation.Token;
		try{
			try{
				await using (var fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)){
					var buffer = new byte[GuildAudio.BufferSize];
					while (true){
						var bytesRead = await input.ReadAsync(buffer, pipingToken);
						if (bytesRead == 0) break;
						var bytes = buffer.AsMemory(0, bytesRead);
						await fileStream.WriteAsync(bytes, pipingToken);
						await output.WriteAsync(bytes, pipingToken);
					}
					await output.FlushAsync(pipingToken);
				}

				pipingToken.ThrowIfCancellationRequested();
				try{
					// Publish without replacing a cache entry another guild completed.
					File.Move(path, finalPath);
				}
				catch (IOException) when (File.Exists(finalPath)){
					// The other completed download is already available for replay.
				}
			}
			catch (Exception e) when (pipingToken.IsCancellationRequested && e is OperationCanceledException or IOException){
				// Skipping can cancel an operation or close a process pipe.
			}
			finally{
				// Close stdin even if opening the cache failed and always remove
				// our partial file. The streaming task observes any failure.
				try{
					await output.DisposeAsync();
				}
				finally{
					File.Delete(path);
				}
			}
		}
		catch{
			await audio.Cancellation.CancelAsync();
			throw;
		}
	}

	/// <summary>
	/// Resumes Paused audio.
	/// </summary>
	/// <param name="guildId">Discord Guild Id</param>
	public void ResumeAudio(ulong guildId) {
		if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)){
			Console.WriteLine("Failed to resume audio for Guild: " + guildId);
			return;
		}

		audio.Paused = false;
	}

	/// <summary>
	/// Pauses the currently streamed audio.
	/// </summary>
	/// <param name="guildId">Discord Guild Id</param>
	public void PauseAudio(ulong guildId) {
		if (!_ffmpegProcesses.TryGetValue(guildId, out var audio)){
			Console.WriteLine("Failed to Pause Audio for Guild: " + guildId);
			return;
		}

		audio.Paused = true;
	}

	/// <summary>
	/// Stops the audio playback
	/// </summary>
	/// <param name="guildId">Discord Guild Id</param>
	public async Task StopAudio(ulong guildId) {
		if (!_ffmpegProcesses.TryRemove(guildId, out var audio)) return;
		await audio.Cancellation.CancelAsync();
		try{
			foreach (var process in new[] { audio.Ffmpeg, audio.Ytdl }){
				if (process is null) continue;
				try{
					if (!process.HasExited) process.Kill(entireProcessTree: true);
					await process.WaitForExitAsync();
				}
				catch (Exception e) when (e is InvalidOperationException or Win32Exception){
					Console.WriteLine("Could not stop audio process: " + e.Message);
				}
			}

			try{
				await Task.WhenAll(audio.StreamingTask, audio.PipingTask);
			}
			catch (Exception){
				// Playback observes streaming failures; cleanup must still finish.
			}
		}
		finally{
			audio.Ffmpeg?.Dispose();
			audio.Ytdl?.Dispose();
			audio.Cancellation.Dispose();
		}
	}
}
