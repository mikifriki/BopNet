using System.Diagnostics;

namespace BopNet.Models;

public class GuildAudio
{
    public Process? Ffmpeg {get; set;}
    public Process? Ytdl {get; set;}
    public string? TimeStamp {get; set;}
    public bool Paused { get; set; }

    public const int BufferSize = 3840;
}