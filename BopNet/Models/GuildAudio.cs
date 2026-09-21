using System.Diagnostics;

namespace BopNet.Models;

public class GuildAudio
{
    public Process? Ffmpeg { get; set; }
    public Process? Ytdl { get; set; }
    public string? TimeStamp { get; set; }
    public bool Paused { get; set; }
    public CancellationTokenSource Cancellation { get; } = new();
    public Task PipingTask { get; set; } = Task.CompletedTask;
    public Task StreamingTask { get; set; } = Task.CompletedTask;

    public const int BufferSize = 3840;  // 20ms stereo 16-bit at 48kHz: 48000 * 0.02 * 2 * 2
}
