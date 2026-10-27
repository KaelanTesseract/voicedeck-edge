using System.Net;
using VoicedeckEdge.Hub.Discord;
using VoicedeckEdge.Hub.Infrastructure;
using VoicedeckEdge.Hub.Server;

namespace VoicedeckEdge.Hub.Tests;

public class DataResetTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("vde-data-").FullName;
    private readonly string _logs = Directory.CreateTempSubdirectory("vde-logs-").FullName;

    public void Dispose()
    {
        Directory.Delete(_data, recursive: true);
        Directory.Delete(_logs, recursive: true);
    }

    private sealed class RecordingHandler(bool fail) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (fail) throw new HttpRequestException("offline");
            Assert.Equal("https://discord.com/api/oauth2/token/revoke", request.RequestUri!.ToString());
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private (TokenStore Tokens, PairingStore Pairings) Seed()
    {
        File.WriteAllText(Path.Combine(_data, "settings.json"), """{"ClientId":"1234567890123456789"}""");
        var tokens = new TokenStore(Path.Combine(_data, "tokens.bin"));
        tokens.Save(new DiscordTokens("1234567890123456789", "access-abc", "refresh-xyz", DateTimeOffset.UtcNow.AddDays(7)));
        var pairings = new PairingStore(Path.Combine(_data, "pairings.json"));
        pairings.Create("file://");
        File.WriteAllText(Path.Combine(_logs, "hub-20261027.log"), "INFO Status: Ready");
        File.WriteAllText(Path.Combine(_logs, "other.txt"), "not ours");
        return (tokens, pairings);
    }

    [Fact]
    public async Task Revokes_at_discord_and_deletes_every_hub_file()
    {
        var (tokens, pairings) = Seed();
        var handler = new RecordingHandler(fail: false);
        var revokedPairings = 0;
        pairings.Revoked += _ => revokedPairings++;

        var result = await DataReset.RunAsync(new DiscordOAuth(new HttpClient(handler)), tokens, pairings, null, _data, _logs, default);

        Assert.True(result.HadSignIn);
        Assert.True(result.Revoked);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Contains(handler.Bodies, b => b.Contains("token=refresh-xyz") && b.Contains("token_type_hint=refresh_token"));
        Assert.Contains(handler.Bodies, b => b.Contains("token=access-abc") && b.Contains("token_type_hint=access_token"));
        Assert.Equal(1, revokedPairings);
        Assert.Empty(Directory.GetFiles(_data));
        Assert.Equal(["other.txt"], Directory.GetFiles(_logs).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Deletes_locally_even_when_discord_is_unreachable()
    {
        var (tokens, pairings) = Seed();

        var result = await DataReset.RunAsync(new DiscordOAuth(new HttpClient(new RecordingHandler(fail: true))), tokens, pairings, null, _data, _logs, default);

        Assert.True(result.HadSignIn);
        Assert.False(result.Revoked);
        Assert.Empty(Directory.GetFiles(_data));
        Assert.Null(tokens.Load());
    }

    [Fact]
    public async Task Works_without_any_sign_in()
    {
        var result = await DataReset.RunAsync(new DiscordOAuth(new HttpClient(new RecordingHandler(fail: true))),
            new TokenStore(Path.Combine(_data, "tokens.bin")), null, null, _data, _logs, default);

        Assert.False(result.HadSignIn);
    }
}

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v0.2.0", "0.1.0", true)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("v0.1.0", "0.1.0", false)]
    [InlineData("v0.0.9", "0.1.0", false)]
    [InlineData("nightly", "0.1.0", false)]
    public void Compares_versions(string tag, string current, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsNewer(tag, current));
}
