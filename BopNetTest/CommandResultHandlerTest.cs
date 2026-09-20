using BopNet;
using Microsoft.Extensions.DependencyInjection;
using NetCord;
using NetCord.Services;

namespace BopNetTest;

public class CommandResultHandlerTest
{
    [Test]
    public async Task NativeLoadException_ProducesShortPrivateMessageWithoutDetails()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var failure = new ExecutionExceptionResult(new DllNotFoundException(
            "/private/native/path/" + new string('x', 5000)));

        var response = await new CommandResultHandler().GetFailMessageAsync(failure, null!, services);

        Assert.Multiple(() =>
        {
            Assert.That(response.Content, Has.Length.LessThanOrEqualTo(2000));
            Assert.That(response.Content, Does.Contain("bot administrator"));
            Assert.That(response.Content, Does.Not.Contain("/private/native/path/"));
            Assert.That(response.Flags, Is.EqualTo(MessageFlags.Ephemeral));
        });
    }

    [TestCase(30)]
    [TestCase(2000)]
    [TestCase(5000)]
    public async Task ValidationFailure_PreservesShortMessagesAndBoundsLongMessages(int length)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var message = new string('x', length);

        var response = await new CommandResultHandler().GetFailMessageAsync(new Failure(message), null!, services);

        Assert.That(response.Content, Is.EqualTo(length <= 2000 ? message : message[..1999] + "…"));
    }

    [Test]
    public async Task Truncation_DoesNotSplitEmoji()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var message = new string('x', 1998) + "🎵more";

        var response = await new CommandResultHandler().GetFailMessageAsync(new Failure(message), null!, services);

        Assert.That(response.Content, Is.EqualTo(new string('x', 1998) + "…"));
    }

    private sealed record Failure(string Message) : IFailResult;
}
