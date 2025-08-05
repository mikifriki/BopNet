using BopNet;
using BopNet.Repository;
using BopNet.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true);
builder.Configuration.SetBasePath(AppContext.BaseDirectory);

builder.Services
    .AddDiscordGateway()
    .AddApplicationCommands()
    .AddSingleton<IAudioService, AudioService>()
    .AddSingleton<IVoiceClientService, VoiceClientService>()
    .AddSingleton<IMusicQueueService, MusicQueueService>()
    .AddDbContext<BotDbContext>(options => options.UseSqlite("Data Source=bot.db"))
    .AddSingleton<IDatabase, DataBaseService>();


var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
    db.Database.EnsureCreated();
}

// Add commands from modules
host.AddModules(typeof(Interactions).Assembly);

// Add handlers to handle the commands
host.UseGatewayEventHandlers();

await host.RunAsync();