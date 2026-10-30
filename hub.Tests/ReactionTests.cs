using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Tests;

public class ReactionTests
{
    // Shape seen in GET_CHANNEL messages (reactions probe 2026-10-30).
    private static JsonObject Reaction(string? id, string name, int count, bool me = false, bool animated = false) => new()
    {
        ["emoji"] = new JsonObject { ["id"] = id, ["name"] = name, ["animated"] = animated },
        ["count"] = count,
        ["count_details"] = new JsonObject { ["burst"] = 0, ["normal"] = count },
        ["me"] = me,
    };

    [Fact]
    public void Unicode_and_server_emojis_are_mapped()
    {
        var msg = new JsonObject
        {
            ["reactions"] = new JsonArray
            {
                Reaction(null, "❤️", 3, me: true),
                Reaction("123456789012345678", "pog", 2),
                Reaction("123456789012345679", "dance", 1, animated: true),
            },
        };
        var r = StateStore.MapReactions(msg);

        Assert.Equal(new ChatReaction("❤️", null, 3, true), r[0]);
        Assert.Equal(new ChatReaction(null, "/img/emojis/123456789012345678", 2, false), r[1]);
        Assert.Equal("/img/emojis/a/123456789012345679", r[2].Image);
    }

    [Fact]
    public void Broken_or_empty_reactions_are_skipped_and_the_list_is_capped()
    {
        var list = new JsonArray { Reaction(null, "", 1), Reaction("not-an-id", "x", 0), new JsonObject { ["count"] = 2 } };
        for (var i = 0; i < 30; i++) list.Add(Reaction(null, "👍", 1));

        var r = StateStore.MapReactions(new JsonObject { ["reactions"] = list });
        Assert.Equal(20, r.Count);
        Assert.All(r, x => Assert.Equal("👍", x.Emoji));
        Assert.Empty(StateStore.MapReactions(new JsonObject()));
    }
}

public class ReactionRefreshTests
{
    private const string Channel = "600000000000000001";

    private static JsonObject Message(string id, params JsonObject[] reactions) => new()
    {
        ["id"] = id,
        ["content"] = "hey",
        ["timestamp"] = "2026-10-30T10:00:00Z",
        ["author"] = new JsonObject { ["id"] = "100000000000000002", ["username"] = "sam" },
        ["reactions"] = new JsonArray(reactions.Select(r => (JsonNode?)r).ToArray()),
    };

    private static JsonObject Heart(int count) => new() { ["emoji"] = new JsonObject { ["id"] = null, ["name"] = "❤️" }, ["count"] = count, ["me"] = false };

    [Fact]
    public void Re_reading_the_channel_updates_reactions_and_emits_only_on_change()
    {
        var store = new StateStore();
        store.SetChat(new JsonObject { ["id"] = Channel, ["name"] = "general", ["type"] = 0, ["messages"] = new JsonArray(Message("900000000000000001")) });
        var emitted = 0;
        store.SectionChanged += (section, _) => { if (section == "chat") emitted++; };

        var fresh = new JsonObject { ["messages"] = new JsonArray(Message("900000000000000001", Heart(2)), Message("900000000000000099", Heart(5))) };
        store.RefreshReactions(Channel, fresh);
        store.RefreshReactions(Channel, fresh.DeepClone().AsObject()); // same again: nothing new
        store.RefreshReactions("600000000000000002", fresh);           // another channel: ignored

        var messages = store.Snapshot()["chat"]!["messages"]!.AsArray();
        Assert.Single(messages);                                    // unknown messages are not added
        Assert.Equal(2, messages[0]!["reactions"]![0]!["count"]!.GetValue<int>());
        Assert.Equal(1, emitted);
    }
}
