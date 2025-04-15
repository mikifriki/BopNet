using BopNet;
using BopNet.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDiscordGateway()
    .AddApplicationCommands()
    .AddSingleton<IAudioService, AudioService>()
    .AddSingleton<IVoiceClientService, VoiceClientService>()
    .AddSingleton<IMusicQueueService, MusicQueueService>();

var host = builder.Build();

// Add commands from modules
host.AddModules(typeof(Interactions).Assembly);

// Add handlers to handle the commands
host.UseGatewayEventHandlers();

await host.RunAsync();