using VoicedeckEdge.Hub.Discord;
using VoicedeckEdge.Hub.Infrastructure;
using VoicedeckEdge.Hub.Server;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Tests;

public class PairingPromptTests
{
    private sealed class ScriptedPrompt : IPairingPrompt
    {
        public Queue<Func<CancellationToken, Task<bool>>> Answers { get; } = new();
        public int Asked { get; private set; }

        public Task<bool> AskAsync(string origin, CancellationToken ct)
        {
            Asked++;
            return Answers.Dequeue()(ct);
        }
    }

    private static (HubServer Server, ScriptedPrompt Prompt) Create()
    {
        var settings = new HubSettings();
        var session = new DiscordSession(settings, new TokenStore(Path.GetTempFileName()), new DiscordOAuth(new HttpClient()));
        var state = new StateStore();
        var prompt = new ScriptedPrompt();
        var server = new HubServer(state, new HubController(session, state), new PairingStore(null), new ImageCache(new HttpClient(), new MediaRegistry()), prompt);
        return (server, prompt);
    }

    [Fact]
    public async Task A_cancelled_question_does_not_block_the_next_one()
    {
        var (server, prompt) = Create();
        prompt.Answers.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => { }); // widget disconnects while asking
            return false;
        });
        prompt.Answers.Enqueue(_ => Task.FromResult(true));

        using var cts = new CancellationTokenSource(50);
        Assert.False(await server.AskPairingAsync("file://", cts.Token));
        Assert.True(await server.AskPairingAsync("file://", CancellationToken.None));
        Assert.Equal(2, prompt.Asked);
    }

    [Fact]
    public async Task A_real_denial_blocks_new_questions_for_a_moment()
    {
        var (server, prompt) = Create();
        prompt.Answers.Enqueue(_ => Task.FromResult(false));

        Assert.False(await server.AskPairingAsync("file://", CancellationToken.None));
        Assert.False(await server.AskPairingAsync("file://", CancellationToken.None)); // cooldown: not asked again
        Assert.Equal(1, prompt.Asked);
    }
}
