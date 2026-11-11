using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Tests;

public class SoundboardTests
{
    private const string Guild = "300000000000000001";

    // Shape of GET_SOUNDBOARD_SOUNDS items (probe 2026-11-11).
    private static JsonObject Sound(string id, string guild, string name, string? emojiId = null, string? emojiName = null, bool available = true) => new()
    {
        ["name"] = name, ["volume"] = 1, ["available"] = available, ["sound_id"] = id, ["guild_id"] = guild,
        ["emoji_id"] = emojiId, ["emoji_name"] = emojiName, ["user_id"] = "100000000000000002",
    };

    [Fact]
    public void Built_in_and_server_sounds_are_mapped_with_server_names()
    {
        var data = new JsonArray
        {
            Sound("1", "0", "quack", emojiName: "🦆"),
            Sound("400000000000000001", Guild, "pog", emojiId: "123456789012345678", available: false),
            Sound("bad", Guild, "x"),
            Sound("400000000000000002", "not-a-guild", "y"),
        };
        var sounds = Soundboard.Map(data, id => id == Guild ? "Game Night" : null);

        Assert.Equal(2, sounds.Count);
        Assert.Equal(new SoundboardSound("1", "0", null, "quack", "🦆", null, true), sounds[0]);
        Assert.Equal("Game Night", sounds[1].GuildName);
        Assert.Equal("/img/emojis/123456789012345678", sounds[1].EmojiImage);
        Assert.False(sounds[1].Available);
    }

    [Theory]
    [InlineData("""{"op":"soundboard.play","guildId":"0","soundId":"1"}""", true)]
    [InlineData("""{"op":"soundboard.play","guildId":"300000000000000001","soundId":"400000000000000001"}""", true)]
    [InlineData("""{"op":"soundboard.play","guildId":"1","soundId":"1"}""", false)]
    [InlineData("""{"op":"soundboard.play","guildId":"0","soundId":"1a"}""", false)]
    [InlineData("""{"op":"soundboard.play","guildId":"0"}""", false)]
    public void Play_command_is_validated(string json, bool valid)
    {
        var parse = () => WidgetCommand.Parse(JsonNode.Parse(json)!.AsObject());
        if (valid) Assert.Equal("soundboard.play", parse().Op);
        else Assert.Equal("invalid_args", Assert.Throws<OpException>(parse).Code);
    }
}
