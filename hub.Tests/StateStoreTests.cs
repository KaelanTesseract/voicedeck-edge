using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Tests;

public class StateStoreTests
{
    private const string Alice = "111111111111111111";
    private const string Bob = "222222222222222222";
    private const string Channel = "333333333333333333";
    private const string Hash = "0123456789abcdef0123456789abcdef";

    // Shape taken from the spike report (voice_states item).
    private static JsonObject VoiceState(string id, string name, bool selfMute = false, string? nick = null, int volume = 100) => new()
    {
        ["nick"] = nick,
        ["mute"] = false,
        ["volume"] = volume,
        ["pan"] = new JsonObject { ["left"] = 1, ["right"] = 1 },
        ["voice_state"] = new JsonObject { ["mute"] = false, ["deaf"] = false, ["self_mute"] = selfMute, ["self_deaf"] = false, ["suppress"] = false },
        ["user"] = new JsonObject { ["id"] = id, ["username"] = name.ToLowerInvariant(), ["global_name"] = name, ["avatar"] = Hash },
    };

    private static JsonObject Message(string id, string time, string content = "hi") => new()
    {
        ["id"] = id,
        ["content"] = content,
        ["timestamp"] = time,
        ["author"] = new JsonObject { ["id"] = Alice, ["username"] = "alice", ["global_name"] = "Alice", ["avatar"] = null },
        ["attachments"] = new JsonArray(),
        ["embeds"] = new JsonArray(),
    };

    private static StateStore WithChannel(params JsonObject[] members)
    {
        var store = new StateStore();
        store.SetVoiceChannel(new JsonObject
        {
            ["id"] = Channel,
            ["name"] = "Lounge",
            ["guild_id"] = "444444444444444444",
            ["voice_states"] = new JsonArray(members.Select(m => (JsonNode?)m).ToArray()),
        }, "My Server");
        return store;
    }

    private static JsonArray Members(StateStore store) => store.Snapshot()["voice"]!["members"]!.AsArray();

    [Fact]
    public void Maps_member_with_local_avatar_path_and_display_name()
    {
        var m = StateStore.MapMember(VoiceState(Alice, "Alice", selfMute: true, nick: "Ali", volume: 150))!;
        Assert.Equal("Ali", m.Name);
        Assert.Equal($"/img/avatars/{Alice}/{Hash}", m.Avatar);
        Assert.True(m.SelfMute);
        Assert.Equal(150, m.Volume);
        Assert.False(m.Speaking);
    }

    [Fact]
    public void Voice_state_create_update_delete()
    {
        var store = WithChannel(VoiceState(Alice, "Alice"));
        store.ApplyVoiceState("VOICE_STATE_CREATE", VoiceState(Bob, "Bob"));
        Assert.Equal(2, Members(store).Count);

        store.ApplyVoiceState("VOICE_STATE_UPDATE", VoiceState(Bob, "Bob", selfMute: true));
        Assert.True(Members(store).Single(m => m!["id"]!.GetValue<string>() == Bob)!["selfMute"]!.GetValue<bool>());

        store.ApplyVoiceState("VOICE_STATE_DELETE", VoiceState(Alice, "Alice"));
        Assert.Equal(Bob, Members(store).Single()!["id"]!.GetValue<string>());
    }

    [Fact]
    public void Update_keeps_speaking_flag_and_speaking_is_sent_separately()
    {
        var store = WithChannel(VoiceState(Alice, "Alice"));
        var sections = new List<string>();
        var speaking = new List<(string, bool)>();
        store.SectionChanged += (s, _) => sections.Add(s);
        store.SpeakingChanged += (u, s) => speaking.Add((u, s));

        store.SetSpeaking(Alice, true);
        store.SetSpeaking(Alice, true); // no duplicate event
        store.SetSpeaking(Bob, true);   // unknown user ignored
        store.ApplyVoiceState("VOICE_STATE_UPDATE", VoiceState(Alice, "Alice", selfMute: true));

        Assert.Equal([(Alice, true)], speaking);
        Assert.Equal(["voice"], sections);
        Assert.True(Members(store).Single()!["speaking"]!.GetValue<bool>());
    }

    [Fact]
    public void Leaving_the_channel_clears_members()
    {
        var store = WithChannel(VoiceState(Alice, "Alice"));
        store.SetVoiceChannel(null, null);
        var voice = store.Snapshot()["voice"]!;
        Assert.Null(voice["channel"]);
        Assert.Empty(voice["members"]!.AsArray());
    }

    [Fact]
    public void Chat_is_sorted_limited_and_only_follows_the_open_channel()
    {
        var store = new StateStore();
        var messages = new JsonArray(Enumerable.Range(0, 60)
            .Select(i => (JsonNode?)Message($"9{i:D17}", $"2026-10-27T10:{i / 60:D2}:{i % 60:D2}+00:00"))
            .Reverse().ToArray());
        store.SetChat(new JsonObject { ["id"] = Channel, ["name"] = "general", ["messages"] = messages });

        var chat = store.Snapshot()["chat"]!["messages"]!.AsArray();
        Assert.Equal(StateStore.MaxMessages, chat.Count);
        Assert.Equal("2026-10-27T10:00:59.000Z", chat.Last()!["time"]!.GetValue<string>());

        store.ApplyMessage("MESSAGE_CREATE", new JsonObject { ["channel_id"] = "999999999999999999", ["message"] = Message("8000000000000000001", "2026-10-27T11:00:00Z") });
        Assert.Equal(StateStore.MaxMessages, store.Snapshot()["chat"]!["messages"]!.AsArray().Count);

        store.ApplyMessage("MESSAGE_CREATE", new JsonObject { ["channel_id"] = Channel, ["message"] = Message("8000000000000000002", "2026-10-27T11:00:00Z", "new") });
        chat = store.Snapshot()["chat"]!["messages"]!.AsArray();
        Assert.Equal(StateStore.MaxMessages, chat.Count);
        Assert.Equal("new", chat.Last()!["content"]!.GetValue<string>());

        store.ApplyMessage("MESSAGE_DELETE", new JsonObject { ["channel_id"] = Channel, ["message"] = new JsonObject { ["id"] = "8000000000000000002" } });
        Assert.DoesNotContain(store.Snapshot()["chat"]!["messages"]!.AsArray(), m => m!["content"]!.GetValue<string>() == "new");
    }

    [Theory]
    [InlineData("https://cdn.discordapp.com/icons/444444444444444444/0123456789abcdef0123456789abcdef.webp", "/img/icons/444444444444444444/0123456789abcdef0123456789abcdef")]
    [InlineData("https://evil.example/icons/444444444444444444/0123456789abcdef0123456789abcdef.webp", null)]
    [InlineData("https://cdn.discordapp.com/icons/444444444444444444/../../x.png", null)]
    [InlineData(null, null)]
    public void Icon_urls_become_local_paths_only_for_discord_cdn(string? url, string? expected) =>
        Assert.Equal(expected, StateStore.IconPath(url));

    [Fact]
    public void Avatar_path_rejects_unexpected_hashes()
    {
        Assert.Null(StateStore.AvatarPath(Alice, null));
        Assert.Null(StateStore.AvatarPath(Alice, "../../secret"));
        Assert.Equal($"/img/avatars/{Alice}/a_{Hash}", StateStore.AvatarPath(Alice, "a_" + Hash));
    }

    [Fact]
    public void Display_name_prefers_nick_then_global_then_username()
    {
        var user = new JsonObject { ["username"] = "alice", ["global_name"] = "Alice" };
        Assert.Equal("Ali", StateStore.DisplayName(user, "Ali"));
        Assert.Equal("Alice", StateStore.DisplayName(user, null));
        user["global_name"] = null;
        Assert.Equal("alice", StateStore.DisplayName(user, ""));
    }

    [Fact]
    public void Reset_forgets_everything_personal()
    {
        var store = WithChannel(VoiceState(Alice, "Alice"));
        store.SetMe(new JsonObject { ["id"] = Alice, ["username"] = "alice" });
        store.SetChat(new JsonObject { ["id"] = Channel, ["name"] = "general", ["messages"] = new JsonArray(Message("9000000000000000001", "2026-10-27T10:00:00Z")) });
        store.Reset();

        var snap = store.Snapshot();
        Assert.Null(snap["me"]);
        Assert.Empty(snap["voice"]!["members"]!.AsArray());
        Assert.Empty(snap["chat"]!["messages"]!.AsArray());
    }
}
