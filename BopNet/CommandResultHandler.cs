using NetCord;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace BopNet;

public sealed class CommandResultHandler : ApplicationCommandResultHandler<ApplicationCommandContext>
{
    public override ValueTask<InteractionMessageProperties> GetFailMessageAsync(
        IFailResult failResult, ApplicationCommandContext context, IServiceProvider services)
    {
        // The base handler logs the original exception before requesting this message.
        var message = failResult is IExceptionResult
            ? "The command failed. Please try again. If the problem persists, contact the bot administrator."
            : failResult.Message;
        if (message.Length <= 2000)
        {
            return new ValueTask<InteractionMessageProperties>(new InteractionMessageProperties
            {
                Content = message,
                Flags = MessageFlags.Ephemeral,
                AllowedMentions = AllowedMentionsProperties.None,
            });
        }

        var length = char.IsHighSurrogate(message[1998]) ? 1998 : 1999;
        message = message[..length] + "…";

        return CreateFailResponse(message);
    }
    private static ValueTask<InteractionMessageProperties> CreateFailResponse(string message) => new(new InteractionMessageProperties
    {
        Content = message,
        Flags = MessageFlags.Ephemeral,
        AllowedMentions = AllowedMentionsProperties.None,
    });
}
