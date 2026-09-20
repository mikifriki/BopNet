using BopNet;
using BopNet.Services.AudioService;
using BopNet.Services.DataBaseService;
using BopNet.Services.MusicQueueService;
using BopNet.Services.TrackCacheService;
using BopNet.Services.VoiceClientService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;

if (args is ["--self-test"])
    return SelfTest.Run();

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

builder.Services
    .AddDiscordGateway(options => options.Intents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates)
    .AddApplicationCommands(options => options.ResultHandler = new CommandResultHandler())
    .AddSingleton<ITrackCacheService, TrackCacheService>()
    .AddSingleton<IAudioService, AudioService>()
    .AddSingleton<IVoiceClientService, VoiceClientService>()
    .AddSingleton<IMusicQueueService, MusicQueueService>()
    .AddScoped<IDatabase>(_ => new DataBaseService("Data Source=bot.db;Pooling=False"));


using var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    // Opening the service creates the schema only when it does not already exist.
    scope.ServiceProvider.GetRequiredService<IDatabase>();
}

// Add commands from modules
host.AddApplicationCommandModule<Interactions>();

await host.RunAsync();
return 0;
