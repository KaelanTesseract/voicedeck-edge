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

public class ServerUnreadTests
{
    private const string Guild = "300000000000000001";
    private const string General = "600000000000000001";
    private const string Clips = "600000000000000002";

    private static JsonObject Notification(string channelId) => new()
    {
        ["channel_id"] = channelId,
        ["title"] = "Sam (#general, Server)",
        ["message"] = new JsonObject
        {
            ["id"] = "900000000000000001",
            ["channel_id"] = channelId,
            ["guild_id"] = Guild,
            ["author"] = new JsonObject { ["id"] = "100000000000000002", ["username"] = "sam" },
            ["content"] = "hey",
        },
    };

    private static JsonNode Unread(StateStore store) => store.Snapshot()["unread"]!;

    [Fact]
    public void Server_notifications_count_per_channel_and_add_up_per_server()
    {
        var store = new StateStore();
        store.ApplyNotification(Notification(General));
        store.ApplyNotification(Notification(General));
        store.ApplyNotification(Notification(Clips));

        Assert.Equal(3, Unread(store)["guilds"]![Guild]!.GetValue<int>());
        Assert.Equal(2, Unread(store)["channels"]![General]!.GetValue<int>());
        Assert.Empty(store.Snapshot()["dms"]!["conversations"]!.AsArray()); // never shown as a DM
    }

    [Fact]
    public void Opening_the_channel_clears_it_and_the_open_channel_does_not_count()
    {
        var store = new StateStore();
        store.ApplyNotification(Notification(General));
        store.ApplyNotification(Notification(Clips));
        store.SetChat(new JsonObject { ["id"] = General, ["name"] = "general", ["type"] = 0, ["messages"] = new JsonArray() });
        store.ApplyNotification(Notification(General));

        Assert.Null(Unread(store)["channels"]![General]);
        Assert.Equal(1, Unread(store)["guilds"]![Guild]!.GetValue<int>());
    }

    [Fact]
    public void Counts_are_capped_and_forgotten_when_the_connection_ends()
    {
        var store = new StateStore();
        for (var i = 0; i < 150; i++) store.ApplyNotification(Notification(General));
        Assert.Equal(StateStore.MaxUnreadCount, Unread(store)["guilds"]![Guild]!.GetValue<int>());

        store.Reset();
        Assert.Empty(Unread(store)["guilds"]!.AsObject());
    }
}
