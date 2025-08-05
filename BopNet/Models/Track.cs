namespace BopNet.Models;
public class Track {
	
	public int Id { get; set; } 
	public string Reference { get; set; } = null!;
	public string? FilePath { get; set; }
	public int PlayCount { get; set; }
	public string? SongName { get; set; }
	public string? Artist { get; set; }
}
