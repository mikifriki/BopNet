namespace BopNet.Repository;

using Microsoft.EntityFrameworkCore;
using Models;

public class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options) {
	public DbSet<Track> Tracks => Set<Track>();
}
