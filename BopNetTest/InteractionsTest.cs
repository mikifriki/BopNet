using System.Reflection;
using BopNet;
using BopNet.Services.MusicQueueService;
using BopNet.Services.VoiceClientService;
using Microsoft.Extensions.Logging.Abstractions;

namespace BopNetTest;

public class InteractionsTest {
	private readonly VoiceClientService _voice = new(NullLogger<Interactions>.Instance);
	private readonly MusicQueueService _queue = new();

	private Interactions CreateModule() => new(NullLogger<Interactions>.Instance, null!, _voice, _queue, null!, null!);

	[Test]
	public async Task ConcurrentCommands_SerializeSameGuildAndAllowOtherGuild() {
		var first = CreateModule();
		var second = CreateModule();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var waitingCommandRan = false;
		var holding = WithGuildLock(first, 1, async () => {
			entered.SetResult();
			await release.Task;
		});
		Task waiting = Task.CompletedTask;
		try{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			waiting = WithGuildLock(second, 1, () => {
				waitingCommandRan = true;
				return Task.CompletedTask;
			});
			await WithGuildLock(second, 2, () => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
			Assert.That(waitingCommandRan, Is.False);
		}
		finally{
			release.TrySetResult();
			await Task.WhenAll(holding, waiting).WaitAsync(TimeSpan.FromSeconds(5));
		}
		Assert.That(waitingCommandRan, Is.True);
	}

	[Test]
	public async Task FailedCommand_ReleasesGuildForNextCommand() {
		Assert.ThrowsAsync<InvalidOperationException>(() => WithGuildLock(CreateModule(), 1,
		() => throw new InvalidOperationException("Controlled failure")));
		await WithGuildLock(CreateModule(), 1, () => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task OldPlaybackCleanup_DoesNotCancelOrClearReplacement() {
		using var previous = new CancellationTokenSource();
		using var replacement = new CancellationTokenSource();
		_voice.GetGuildVoiceClient(1).Cancellation = replacement;
		_queue.AddMusicQueue(1, "new track");
		await Invoke(CreateModule(), "FinishPlayback", 1UL, previous);
		Assert.Multiple(() => {
			Assert.That(_voice.GetGuildVoiceClient(1).Cancellation, Is.SameAs(replacement));
			Assert.That(replacement.IsCancellationRequested, Is.False);
			Assert.That(_queue.GetNextTrack(1), Is.EqualTo("new track"));
		});
		_voice.GetGuildVoiceClient(1).Cancellation = null;
	}

	// Exercise the command coordination without connecting a gateway or loading voice libraries.
	private static Task WithGuildLock(Interactions module, ulong guild, Func<Task> action) =>
		Invoke(module, "WithGuildLock", guild, action);

	private static Task Invoke(Interactions module, string method, params object[] arguments) =>
		(Task) typeof(Interactions).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(module, arguments)!;
}
