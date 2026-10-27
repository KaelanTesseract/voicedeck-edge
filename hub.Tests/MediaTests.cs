using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.Server;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Tests;

public class MediaTests
{
    private const string Alice = "111111111111111111";

    private static JsonObject Message(JsonArray? attachments = null, JsonArray? embeds = null, string content = "") => new()
    {
        ["id"] = "900000000000000001",
        ["content"] = content,
        ["timestamp"] = "2026-10-27T10:00:00Z",
        ["author"] = new JsonObject { ["id"] = Alice, ["username"] = "alice" },
        ["attachments"] = attachments ?? new JsonArray(),
        ["embeds"] = embeds ?? new JsonArray(),
    };

    [Theory]
    [InlineData("https://cdn.discordapp.com/attachments/1/2/cat.png", true)]
    [InlineData("https://media.discordapp.net/attachments/1/2/cat.png?ex=1&is=2&hm=3", true)]
    [InlineData("https://images-ext-1.discordapp.net/external/abc/https/example.com/x.png", true)]
    [InlineData("http://cdn.discordapp.com/attachments/1/2/cat.png", false)]
    [InlineData("https://cdn.discordapp.com:8443/x.png", false)]
    [InlineData("https://evil.example/x.png", false)]
    [InlineData("https://discordapp.net.evil.example/x.png", false)]
    [InlineData("https://images-ext-1.discordapp.net.evil.example/x.png", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData(null, false)]
    public void Only_discord_image_hosts_are_registered(string? url, bool allowed) =>
        Assert.Equal(allowed, new MediaRegistry().Register(url) != null);

    [Fact]
    public void Registered_image_resolves_to_its_url_and_unknown_tokens_do_not()
    {
        var media = new MediaRegistry();
        var cache = new ImageCache(new HttpClient(), media);
        var path = media.Register("https://cdn.discordapp.com/attachments/1/2/cat.png")!;

        Assert.Matches("^/img/m/[0-9a-f]{32}$", path);
        Assert.Equal("https://cdn.discordapp.com/attachments/1/2/cat.png", cache.Resolve(path));
        Assert.Null(cache.Resolve("/img/m/" + new string('0', 32)));
    }

    [Fact]
    public void Large_proxy_images_are_scaled_down()
    {
        var url = MediaRegistry.Resize("https://media.discordapp.net/attachments/1/2/big.png?ex=1", 1920, 1080);
        Assert.EndsWith("&width=480&height=270", url);
        Assert.Equal("https://media.discordapp.net/a.png", MediaRegistry.Resize("https://media.discordapp.net/a.png", 300, 200));
    }

    [Theory]
    [InlineData("/img/emojis/123456789012345678", "https://cdn.discordapp.com/emojis/123456789012345678.webp?size=64&quality=lossless")]
    [InlineData("/img/emojis/a/123456789012345678", "https://cdn.discordapp.com/emojis/123456789012345678.gif?size=64&quality=lossless")]
    [InlineData("/img/emojis/../../etc", null)]
    [InlineData("/img/emojis/12", null)]
    public void Custom_emojis_resolve_to_the_discord_cdn(string path, string? expected) =>
        Assert.Equal(expected, new ImageCache(new HttpClient(), new MediaRegistry()).Resolve(path));

    [Fact]
    public void Image_attachments_become_images_other_files_stay_a_count()
    {
        var msg = StateStore.MapMessage(Message(new JsonArray(
            new JsonObject { ["filename"] = "cat.png", ["content_type"] = "image/png", ["proxy_url"] = "https://media.discordapp.net/attachments/1/2/cat.png", ["width"] = 800, ["height"] = 600 },
            new JsonObject { ["filename"] = "notes.pdf", ["content_type"] = "application/pdf", ["url"] = "https://cdn.discordapp.com/attachments/1/2/notes.pdf" })), new MediaRegistry())!;

        var image = Assert.Single(msg.Images);
        Assert.StartsWith("/img/m/", image.Path);
        Assert.Equal((800, 600), (image.Width, image.Height));
        Assert.Equal(1, msg.Attachments);
    }

    [Fact]
    public void Pasted_image_link_shows_the_picture_instead_of_the_url()
    {
        var msg = StateStore.MapMessage(Message(embeds: new JsonArray(new JsonObject
        {
            ["type"] = "image",
            ["thumbnail"] = new JsonObject { ["proxy_url"] = "https://images-ext-1.discordapp.net/external/x/https/example.com/a.png", ["width"] = 300, ["height"] = 200 },
        }), content: "https://example.com/a.png"), new MediaRegistry())!;

        Assert.Single(msg.Images);
        Assert.Equal("", msg.Content);
        Assert.False(msg.HasEmbed);
    }

    [Fact]
    public void Link_cards_without_picture_stay_a_hint_and_foreign_hosts_are_ignored()
    {
        var msg = StateStore.MapMessage(Message(embeds: new JsonArray(
            new JsonObject { ["type"] = "rich", ["title"] = "News" },
            new JsonObject { ["type"] = "image", ["thumbnail"] = new JsonObject { ["proxy_url"] = "https://evil.example/a.png" } })), new MediaRegistry())!;

        Assert.Empty(msg.Images);
        Assert.True(msg.HasEmbed);
    }
}
