using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Tests;

public class WidgetCommandTests
{
    private const string Id = "123456789012345678";

    private static WidgetCommand Parse(string json) => WidgetCommand.Parse((JsonObject)JsonNode.Parse(json)!);

    private static string ErrorOf(string json) => Assert.Throws<OpException>(() => Parse(json)).Code;

    [Theory]
    [InlineData("""{"op":"voice.toggleMute"}""")]
    [InlineData("""{"op":"voice.toggleDeaf"}""")]
    [InlineData("""{"op":"voice.leave"}""")]
    [InlineData("""{"op":"guilds.list"}""")]
    [InlineData("""{"op":"chat.close"}""")]
    public void Accepts_commands_without_arguments(string json) => Assert.NotNull(Parse(json));

    [Theory]
    [InlineData("""{"op":"rpc.raw","cmd":"AUTHORIZE"}""")]
    [InlineData("""{"op":"VOICE.TOGGLEMUTE"}""")]
    [InlineData("""{"op":42}""")]
    [InlineData("""{}""")]
    public void Rejects_unknown_operations(string json) => Assert.Equal("unknown_op", ErrorOf(json));

    [Fact]
    public void Join_requires_a_snowflake_channel_id()
    {
        Assert.Equal(Id, Parse($$"""{"op":"voice.join","channelId":"{{Id}}"}""").ChannelId);
        Assert.Equal("invalid_args", ErrorOf("""{"op":"voice.join","channelId":"../../etc"}"""));
        Assert.Equal("invalid_args", ErrorOf("""{"op":"voice.join","channelId":123456789012345678}"""));
        Assert.Equal("invalid_args", ErrorOf("""{"op":"voice.join"}"""));
    }

    [Theory]
    [InlineData(150, 150)]
    [InlineData(-20, 0)]
    [InlineData(999, 200)]
    [InlineData(99.6, 100)]
    public void Volume_is_clamped_to_discords_range(double input, int expected)
    {
        var cmd = Parse($$"""{"op":"user.setVolume","userId":"{{Id}}","volume":{{input.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");
        Assert.Equal(expected, cmd.Volume);
    }

    [Fact]
    public void Volume_must_be_a_number() =>
        Assert.Equal("invalid_args", ErrorOf($$"""{"op":"user.setVolume","userId":"{{Id}}","volume":"loud"}"""));

    [Fact]
    public void Mode_only_accepts_discord_modes()
    {
        Assert.Equal("PUSH_TO_TALK", Parse("""{"op":"voice.setMode","mode":"PUSH_TO_TALK"}""").Mode);
        Assert.Equal("invalid_args", ErrorOf("""{"op":"voice.setMode","mode":"push_to_talk"}"""));
    }

    [Fact]
    public void User_mute_requires_a_boolean()
    {
        Assert.True(Parse($$"""{"op":"user.setMute","userId":"{{Id}}","mute":true}""").Mute);
        Assert.Equal("invalid_args", ErrorOf($$"""{"op":"user.setMute","userId":"{{Id}}","mute":"yes"}"""));
    }
}
