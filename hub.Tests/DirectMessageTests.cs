using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.Discord;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Tests;

public class DirectMessageTests
{
    private const string DmA = "500000000000000001";
    private const string DmB = "500000000000000002";
    private const string Sam = "100000000000000002";
    private const string Hash = "0123456789abcdef0123456789abcdef";

    // Shape taken from the DM spike report (NOTIFICATION_CREATE).
    private static JsonObject Notification(string channelId, string? guildId = null, string title = "Sam", string time = "2026-10-27T20:00:00Z") => new()
    {
        ["channel_id"] = channelId,
        ["title"] = title,
        ["message"] = new JsonObject
        {
            ["id"] = "900000000000000001",
            ["channel_id"] = channelId,
            ["guild_id"] = guildId,
            ["author"] = new JsonObject { ["id"] = Sam, ["username"] = "sam", ["global_name"] = "Sam", ["avatar"] = Hash },
            ["content"] = "hey",
            ["timestamp"] = time,
        },
    };

    private static JsonArray Conversations(StateStore store) => store.Snapshot()["dms"]!["conversations"]!.AsArray();

    [Fact]
    public void Direct_message_notification_creates_an_unread_conversation()
    {
        var store = new StateStore();
        store.ApplyNotification(Notification(DmA));
        store.ApplyNotification(Notification(DmA));

        var c = Assert.Single(Conversations(store))!;
        Assert.Equal(DmA, c["channelId"]!.GetValue<string>());
        Assert.Equal("Sam", c["name"]!.GetValue<string>());
        Assert.Equal($"/img/avatars/{Sam}/{Hash}", c["avatar"]!.GetValue<string>());
        Assert.Equal(2, c["unread"]!.GetValue<int>());
    }

    [Fact]
    public void Server_notifications_are_ignored()
    {
        var store = new StateStore();
        store.ApplyNotification(Notification(DmA, guildId: "300000000000000001"));
        Assert.Empty(Conversations(store));
    }

    [Fact]
    public void Newest_conversation_comes_first_and_the_list_is_capped()
    {
        var store = new StateStore();
        for (var i = 0; i < StateStore.MaxDirectConversations + 5; i++)
            store.ApplyNotification(Notification($"5000000000000{i:D5}"));
        store.ApplyNotification(Notification(DmB));

        var list = Conversations(store);
        Assert.Equal(StateStore.MaxDirectConversations, list.Count);
        Assert.Equal(DmB, list[0]!["channelId"]!.GetValue<string>());
    }

    [Fact]
    public void Opening_the_dm_marks_it_read_and_new_messages_there_stay_read()
    {
        var store = new StateStore();
        store.ApplyNotification(Notification(DmA));
        // DM channels have type 1 and often no name over RPC
        store.SetChat(new JsonObject { ["id"] = DmA, ["name"] = "", ["type"] = 1, ["messages"] = new JsonArray() });

        Assert.Equal(0, Conversations(store)[0]!["unread"]!.GetValue<int>());
        var chat = store.Snapshot()["chat"]!;
        Assert.True(chat["direct"]!.GetValue<bool>());
        Assert.Equal("Sam", chat["channelName"]!.GetValue<string>());

        store.ApplyNotification(Notification(DmA));
        Assert.Equal(0, Conversations(store)[0]!["unread"]!.GetValue<int>());
    }

    [Fact]
    public void Dm_list_is_forgotten_when_the_connection_ends()
    {
        var store = new StateStore();
        store.ApplyNotification(Notification(DmA));
        store.Reset();
        Assert.Empty(Conversations(store));
    }

    [Theory]
    [InlineData(new[] { "rpc", "rpc.voice.read", "rpc.voice.write", "messages.read", "rpc.notifications.read" }, true)]
    [InlineData(new[] { "rpc", "rpc.voice.read", "rpc.voice.write", "messages.read" }, false)]
    public void Older_sign_ins_without_the_notification_scope_need_a_new_authorization(string[] scopes, bool enough) =>
        Assert.Equal(enough, DiscordOAuth.HasRequiredScopes(new JsonObject { ["scopes"] = new JsonArray(scopes.Select(s => (JsonNode?)s).ToArray()) }));

    [Fact]
    public void Missing_scope_list_is_not_enough() => Assert.False(DiscordOAuth.HasRequiredScopes(new JsonObject()));
}
