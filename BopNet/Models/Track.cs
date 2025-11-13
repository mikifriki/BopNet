namespace BopNet.Models;

public class Track
{
    public int Id { get; set; }
    public string Reference { get; set; } = null!;
    public string FullUrl { get; set; } = null!;
    public int PlayCount { get; set; } = 1;
    public string? FilePath { get; set; }
    public string? SongName { get; set; }
    public string? Artist { get; set; }
}