using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VoicedeckEdge.Hub.Discord;
using VoicedeckEdge.Hub.Server;

namespace VoicedeckEdge.Hub.State;

internal sealed record Me(string Id, string Name, string? Avatar);

internal sealed record Member(
    string Id, string Name, string? Avatar, bool Speaking,
    bool SelfMute, bool SelfDeaf, bool ServerMute, bool ServerDeaf,
    bool LocalMute, int Volume);

internal sealed record VoiceChannel(string Id, string Name, string? GuildId, string? GuildName);

internal sealed record ChatImage(string Path, int Width, int Height);

/// <summary>A DM conversation the hub learned about from a notification. Memory only.</summary>
/// <summary>A microphone or speaker as Discord lists it (id is Discord's device id, "default" = Windows default).</summary>
internal sealed record AudioDevice(string Id, string Name);

/// <summary>One side of Discord's audio settings: chosen device, volume and the devices to choose from.</summary>
internal sealed record AudioSide(string? DeviceId, int Volume, IReadOnlyList<AudioDevice> Devices);

internal sealed record DirectConversation(string ChannelId, string Name, string? Avatar, string LastTime, int Unread);

/// <summary>A reaction under a message: a unicode <paramref name="Emoji"/> or a server emoji <paramref name="Image"/>.</summary>
internal sealed record ChatReaction(string? Emoji, string? Image, int Count, bool Me);

/// <summary>Attachments counts only non-image files; images are listed in <see cref="Images"/>.</summary>
internal sealed record ChatMessage(
    string Id, string AuthorId, string Author, string? Avatar, string Content,
    string Time, bool Edited, int Attachments, bool HasEmbed, IReadOnlyList<ChatImage> Images,
    IReadOnlyList<ChatReaction> Reactions);

/// <summary>
/// The widget-facing state. Lives only in memory. Every mutation raises <see cref="SectionChanged"/>
/// with the full section (small payloads), except speaking which is high-frequency and sent alone.
/// </summary>
internal sealed partial class StateStore(MediaRegistry? media = null)
{
    private const int MaxImagesPerMessage = 4;
    private readonly MediaRegistry _media = media ?? new MediaRegistry();

    public const int MaxMessages = 50;
    private const int MaxContentLength = 2000;

    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly object _gate = new();
    private string _status = "not_configured";
    private Me? _me;
    private VoiceChannel? _channel;
    private string _connection = "DISCONNECTED";
    private readonly List<Member> _members = [];
    private bool _mute, _deaf;
    private string _mode = "VOICE_ACTIVITY";
    private AudioSide _input = new(null, 100, []);
    private AudioSide _output = new(null, 100, []);
    public const int MaxAudioDevices = 32;
    private string? _chatChannelId, _chatChannelName;
    private bool _chatIsDirect;
    private readonly List<ChatMessage> _messages = [];
    private readonly List<DirectConversation> _dms = [];
    public const int MaxDirectConversations = 20;
    // Unread server messages per channel (from notifications), kept only in memory like the DM list.
    private readonly Dictionary<string, (string GuildId, int Count)> _unread = [];
    public const int MaxUnreadCount = 99;

    public event Action<string, JsonNode?>? SectionChanged;
    public event Action<string, bool>? SpeakingChanged;

    public bool Mute { get { lock (_gate) return _mute; } }
    public bool Deaf { get { lock (_gate) return _deaf; } }
    public string? VoiceChannelId { get { lock (_gate) return _channel?.Id; } }
    public string? ChatChannelId { get { lock (_gate) return _chatChannelId; } }

    public JsonObject Snapshot()
    {
        lock (_gate)
        {
            return new JsonObject
            {
                ["status"] = StatusSection(),
                ["me"] = ToNode(_me),
                ["voice"] = VoiceSection(),
                ["settings"] = SettingsSection(),
                ["chat"] = ChatSection(),
                ["dms"] = DmSection(),
                ["unread"] = UnreadSection(),
            };
        }
    }

    // ---------- status / me ----------

    public void SetStatus(string status)
    {
        lock (_gate)
        {
            if (_status == status) return;
            _status = status;
        }
        Emit("status", StatusSection());
    }

    public void SetMe(JsonObject user)
    {
        var id = user.Str("id");
        lock (_gate) _me = id == null ? null : new Me(id, DisplayName(user, null), AvatarPath(id, user.Str("avatar")));
        Emit("me", ToNode(_me));
    }

    /// <summary>Discord connection lost: forget everything except the status.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _me = null;
            _channel = null;
            _connection = "DISCONNECTED";
            _members.Clear();
            _mute = _deaf = false;
            _chatChannelId = _chatChannelName = null;
            _chatIsDirect = false;
            _messages.Clear();
            _dms.Clear(); // the DM list lives only as long as the connection (privacy choice)
            _unread.Clear();
        }
        Emit("me", null);
        Emit("voice", VoiceSection());
        Emit("settings", SettingsSection());
        Emit("chat", ChatSection());
        Emit("dms", DmSection());
        Emit("unread", UnreadSection());
    }

    // ---------- direct messages ----------

    /// <summary>
    /// NOTIFICATION_CREATE: a direct message adds or refreshes the conversation; a server message counts
    /// towards that server's red badge. Either is unread unless it is the chat open in the widget.
    /// </summary>
    public void ApplyNotification(JsonObject data)
    {
        var message = data["message"] as JsonObject;
        if (message == null) return;
        if (message.Str("guild_id") is { } guildId)
        {
            CountUnread(guildId, data.Str("channel_id") ?? message.Str("channel_id"));
            return;
        }
        var channelId = data.Str("channel_id") ?? message.Str("channel_id");
        var author = message["author"] as JsonObject;
        var authorId = author.Str("id");
        if (channelId == null || !Json.IsSnowflake(channelId) || authorId == null) return;

        lock (_gate)
        {
            var i = _dms.FindIndex(d => d.ChannelId == channelId);
            var unread = (i >= 0 ? _dms[i].Unread : 0) + (channelId == _chatChannelId ? 0 : 1);
            var name = data.Str("title") is { Length: > 0 } title ? title : DisplayName(author!, null);
            var conversation = new DirectConversation(channelId, name, AvatarPath(authorId, author.Str("avatar")),
                NormalizeTime(message.Str("timestamp")), unread);
            if (i >= 0) _dms.RemoveAt(i);
            _dms.Insert(0, conversation);
            if (_dms.Count > MaxDirectConversations) _dms.RemoveAt(_dms.Count - 1);
        }
        Emit("dms", DmSection());
    }

    private void CountUnread(string guildId, string? channelId)
    {
        if (!Json.IsSnowflake(guildId) || channelId == null || !Json.IsSnowflake(channelId)) return;
        lock (_gate)
        {
            if (channelId == _chatChannelId) return;
            var count = _unread.TryGetValue(channelId, out var entry) ? entry.Count : 0;
            _unread[channelId] = (guildId, Math.Min(count + 1, MaxUnreadCount));
        }
        Emit("unread", UnreadSection());
    }

    private void MarkChannelRead(string channelId)
    {
        bool changed;
        lock (_gate) changed = _unread.Remove(channelId);
        if (changed) Emit("unread", UnreadSection());
    }

    private void MarkDirectRead(string channelId)
    {
        var changed = false;
        lock (_gate)
        {
            var i = _dms.FindIndex(d => d.ChannelId == channelId);
            if (i >= 0 && _dms[i].Unread > 0)
            {
                _dms[i] = _dms[i] with { Unread = 0 };
                changed = true;
            }
        }
        if (changed) Emit("dms", DmSection());
    }

    // ---------- voice ----------

    /// <summary>Sets the joined voice channel from GET_CHANNEL data (or leaves it with null).</summary>
    public void SetVoiceChannel(JsonObject? channel, string? guildName)
    {
        lock (_gate)
        {
            _members.Clear();
            if (channel?.Str("id") is { } id)
            {
                _channel = new VoiceChannel(id, channel.Str("name") ?? "", channel.Str("guild_id"), guildName);
                foreach (var vs in channel.Arr("voice_states").OfType<JsonObject>())
                    if (MapMember(vs) is { } m) _members.Add(m);
            }
            else
            {
                _channel = null;
            }
        }
        Emit("voice", VoiceSection());
    }

    /// <summary>VOICE_STATE_CREATE / _UPDATE / _DELETE.</summary>
    public void ApplyVoiceState(string evt, JsonObject data)
    {
        if (MapMember(data) is not { } member) return;
        lock (_gate)
        {
            var i = _members.FindIndex(m => m.Id == member.Id);
            if (evt == "VOICE_STATE_DELETE")
            {
                if (i < 0) return;
                _members.RemoveAt(i);
            }
            else if (i >= 0)
            {
                _members[i] = member with { Speaking = _members[i].Speaking };
            }
            else
            {
                _members.Add(member);
            }
        }
        Emit("voice", VoiceSection());
    }

    public void SetSpeaking(string userId, bool speaking)
    {
        lock (_gate)
        {
            var i = _members.FindIndex(m => m.Id == userId);
            if (i < 0 || _members[i].Speaking == speaking) return;
            _members[i] = _members[i] with { Speaking = speaking };
        }
        SpeakingChanged?.Invoke(userId, speaking);
    }

    public void SetConnection(string state)
    {
        lock (_gate)
        {
            if (_connection == state) return;
            _connection = state;
        }
        Emit("voice", VoiceSection());
    }

    /// <summary>Optimistic local update after SET_USER_VOICE_SETTINGS (Discord sends no event for it).</summary>
    public void UpdateMemberLocal(string userId, int? volume, bool? localMute)
    {
        lock (_gate)
        {
            var i = _members.FindIndex(m => m.Id == userId);
            if (i < 0) return;
            _members[i] = _members[i] with
            {
                Volume = volume ?? _members[i].Volume,
                LocalMute = localMute ?? _members[i].LocalMute,
            };
        }
        Emit("voice", VoiceSection());
    }

    // ---------- own voice settings ----------

    /// <summary>GET_VOICE_SETTINGS / VOICE_SETTINGS_UPDATE.</summary>
    public void ApplyVoiceSettings(JsonObject data)
    {
        lock (_gate)
        {
            _mute = data.Bool("mute", _mute);
            _deaf = data.Bool("deaf", _deaf);
            _mode = data["mode"].Str("type") ?? _mode;
            _input = ReadAudioSide(data["input"] as JsonObject, _input, maxVolume: 100);
            _output = ReadAudioSide(data["output"] as JsonObject, _output, maxVolume: 200);
        }
        Emit("settings", SettingsSection());
    }

    // ---------- chat ----------

    /// <summary>Opens a text channel from GET_CHANNEL data (or closes the chat with null).</summary>
    public void SetChat(JsonObject? channel)
    {
        lock (_gate)
        {
            _messages.Clear();
            _media.Clear(); // images of the previous channel are no longer needed
            _chatChannelId = channel?.Str("id");
            _chatIsDirect = channel.Int("type", -1) is 1 or 3;
            // DM channels often have no name over RPC: use the name known from the notification.
            var known = _dms.FirstOrDefault(d => d.ChannelId == _chatChannelId)?.Name;
            _chatChannelName = channel.Str("name") is { Length: > 0 } n ? n : known;
            if (channel != null)
            {
                var mapped = channel.Arr("messages").OfType<JsonObject>().Select(m => MapMessage(m, _media)).OfType<ChatMessage>();
                _messages.AddRange(mapped.OrderBy(m => m.Time, StringComparer.Ordinal).TakeLast(MaxMessages));
            }
        }
        Emit("chat", ChatSection());
        if (_chatIsDirect && _chatChannelId != null) MarkDirectRead(_chatChannelId);
        else if (_chatChannelId != null) MarkChannelRead(_chatChannelId);
    }

    /// <summary>MESSAGE_CREATE / _UPDATE / _DELETE (data = { channel_id, message }).</summary>
    public void ApplyMessage(string evt, JsonObject data)
    {
        var raw = data["message"] as JsonObject;
        var id = raw.Str("id");
        if (id == null) return;
        lock (_gate)
        {
            if (data.Str("channel_id") != _chatChannelId) return;
            var i = _messages.FindIndex(m => m.Id == id);
            switch (evt)
            {
                case "MESSAGE_DELETE":
                    if (i < 0) return;
                    _messages.RemoveAt(i);
                    break;
                case "MESSAGE_UPDATE":
                    if (i < 0 || MapMessage(raw!, _media) is not { } updated) return;
                    _messages[i] = updated;
                    break;
                default:
                    if (i >= 0 || MapMessage(raw!, _media) is not { } created) return;
                    _messages.Add(created);
                    if (_messages.Count > MaxMessages) _messages.RemoveAt(0);
                    break;
            }
        }
        Emit("chat", ChatSection());
    }

    // ---------- mapping (static, unit-tested) ----------

    internal static Member? MapMember(JsonObject vs)
    {
        var user = vs["user"] as JsonObject;
        var id = user.Str("id");
        if (id == null) return null;
        var state = vs["voice_state"];
        return new Member(
            id,
            DisplayName(user!, vs.Str("nick")),
            AvatarPath(id, user.Str("avatar")),
            Speaking: false,
            SelfMute: state.Bool("self_mute"),
            SelfDeaf: state.Bool("self_deaf"),
            ServerMute: state.Bool("mute") || state.Bool("suppress"),
            ServerDeaf: state.Bool("deaf"),
            LocalMute: vs.Bool("mute"),
            Volume: Math.Clamp(vs.Int("volume", 100), 0, 200));
    }

    internal static ChatMessage? MapMessage(JsonObject msg, MediaRegistry media)
    {
        var id = msg.Str("id");
        var author = msg["author"] as JsonObject;
        var authorId = author.Str("id");
        if (id == null || authorId == null) return null;
        var content = msg.Str("content") ?? "";
        if (content.Length > MaxContentLength) content = content[..MaxContentLength];
        var images = new List<ChatImage>();
        var otherFiles = 0;
        foreach (var a in msg.Arr("attachments").OfType<JsonObject>())
        {
            if (IsImageAttachment(a) && images.Count < MaxImagesPerMessage
                && media.Register(a.Str("proxy_url") ?? a.Str("url"), a.Int("width"), a.Int("height")) is { } path)
                images.Add(new ChatImage(path, a.Int("width"), a.Int("height")));
            else
                otherFiles++;
        }
        var otherEmbeds = 0;
        foreach (var e in msg.Arr("embeds").OfType<JsonObject>())
        {
            // Picture embeds (pasted image links, GIF services) show their image; link cards stay a hint.
            var picture = e["image"] as JsonObject
                ?? (e.Str("type") is "image" or "gifv" ? e["thumbnail"] as JsonObject : null);
            if (picture != null && images.Count < MaxImagesPerMessage
                && media.Register(picture.Str("proxy_url") ?? picture.Str("url"), picture.Int("width"), picture.Int("height")) is { } path)
                images.Add(new ChatImage(path, picture.Int("width"), picture.Int("height")));
            else
                otherEmbeds++;
        }
        // A message that is only a pasted image link would otherwise show the raw URL above the picture.
        if (images.Count > 0 && Uri.IsWellFormedUriString(content.Trim(), UriKind.Absolute) && otherEmbeds == 0)
            content = "";

        return new ChatMessage(
            id,
            authorId,
            DisplayName(author!, msg.Str("nick")),
            AvatarPath(authorId, author.Str("avatar")),
            content,
            NormalizeTime(msg.Str("timestamp")),
            Edited: msg.Str("edited_timestamp") != null,
            Attachments: otherFiles,
            HasEmbed: otherEmbeds > 0,
            Images: images,
            Reactions: MapReactions(msg));
    }

    private const int MaxReactionsPerMessage = 20;

    /// <summary>reactions: [{ emoji: { id, name, animated }, count, me }] – server emojis go through the image proxy.</summary>
    internal static IReadOnlyList<ChatReaction> MapReactions(JsonObject msg) =>
        msg.Arr("reactions").OfType<JsonObject>()
            .Select(r =>
            {
                var emoji = r["emoji"] as JsonObject;
                var count = Math.Clamp(r.Int("count"), 0, 9999);
                if (emoji == null || count == 0) return null;
                var id = emoji.Str("id");
                if (id != null && Json.IsSnowflake(id))
                    return new ChatReaction(null, $"/img/emojis/{(emoji.Bool("animated") ? "a/" : "")}{id}", count, r.Bool("me"));
                return emoji.Str("name") is { Length: > 0 and <= 32 } name ? new ChatReaction(name, null, count, r.Bool("me")) : null;
            })
            .OfType<ChatReaction>()
            .Take(MaxReactionsPerMessage)
            .ToList();

    private static bool IsImageAttachment(JsonObject a) =>
        a.Str("content_type")?.StartsWith("image/", StringComparison.Ordinal) == true
        || Regex.IsMatch(a.Str("filename") ?? "", @"\.(png|jpe?g|gif|webp)$", RegexOptions.IgnoreCase);

    internal static string DisplayName(JsonObject user, string? nick) =>
        !string.IsNullOrWhiteSpace(nick) ? nick
        : user.Str("global_name") is { Length: > 0 } g ? g
        : user.Str("username") ?? "?";

    [GeneratedRegex("^(a_)?[0-9a-f]{32}$")]
    internal static partial Regex ImageHash();

    /// <summary>Local hub path for a user avatar, or null when the user has the default avatar.</summary>
    internal static string? AvatarPath(string userId, string? hash) =>
        Json.IsSnowflake(userId) && hash != null && ImageHash().IsMatch(hash) ? $"/img/avatars/{userId}/{hash}" : null;

    /// <summary>Converts Discord's icon_url (https://cdn.discordapp.com/icons/{id}/{hash}.ext) to a local hub path.</summary>
    internal static string? IconPath(string? iconUrl)
    {
        if (iconUrl == null || !Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri) || uri.Host != "cdn.discordapp.com")
            return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 3 || parts[0] != "icons") return null;
        var hash = Path.GetFileNameWithoutExtension(parts[2]);
        return Json.IsSnowflake(parts[1]) && ImageHash().IsMatch(hash) ? $"/img/icons/{parts[1]}/{hash}" : null;
    }

    private static string NormalizeTime(string? timestamp) =>
        DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
            : "";

    // ---------- sections ----------

    private JsonNode StatusSection() => new JsonObject { ["discord"] = _status };

    private JsonNode VoiceSection()
    {
        lock (_gate)
        {
            return new JsonObject
            {
                ["channel"] = ToNode(_channel),
                ["connection"] = _connection,
                ["members"] = ToNode(_members),
            };
        }
    }

    private JsonNode SettingsSection()
    {
        lock (_gate)
        {
            return new JsonObject
            {
                ["mute"] = _mute, ["deaf"] = _deaf, ["mode"] = _mode,
                ["input"] = ToNode(_input), ["output"] = ToNode(_output),
            };
        }
    }

    /// <summary>Is <paramref name="deviceId"/> one of the devices Discord currently offers on that side?</summary>
    public bool HasAudioDevice(bool input, string deviceId)
    {
        lock (_gate) return (input ? _input : _output).Devices.Any(d => d.Id == deviceId);
    }

    /// <summary>
    /// GET_VOICE_SETTINGS / VOICE_SETTINGS_UPDATE: { device_id, volume, available_devices: [{ id, name }] }.
    /// Missing parts keep their previous value (updates may only carry what changed).
    /// </summary>
    internal static AudioSide ReadAudioSide(JsonObject? side, AudioSide previous, int maxVolume)
    {
        if (side == null) return previous;
        var devices = side["available_devices"] is JsonArray list
            ? list.OfType<JsonObject>()
                .Select(d => d.Str("id") is { Length: > 0 } id ? new AudioDevice(id, d.Str("name") is { Length: > 0 } n ? n : id) : null)
                .OfType<AudioDevice>()
                .Take(MaxAudioDevices)
                .ToList()
            : previous.Devices;
        var volume = side["volume"] is JsonValue v
                     && double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d)
            ? (int)Math.Clamp(Math.Round(d), 0, maxVolume)
            : previous.Volume;
        return new AudioSide(side.Str("device_id") ?? previous.DeviceId, volume, devices);
    }

    /// <summary>{ guilds: { id: count }, channels: { id: count } } – counts capped at <see cref="MaxUnreadCount"/>.</summary>
    private JsonNode UnreadSection()
    {
        lock (_gate)
        {
            var guilds = new JsonObject();
            var channels = new JsonObject();
            foreach (var (channelId, (guildId, count)) in _unread)
            {
                channels[channelId] = count;
                guilds[guildId] = Math.Min((guilds[guildId]?.GetValue<int>() ?? 0) + count, MaxUnreadCount);
            }
            return new JsonObject { ["guilds"] = guilds, ["channels"] = channels };
        }
    }

    private JsonNode DmSection()
    {
        lock (_gate) return new JsonObject { ["conversations"] = ToNode(_dms) };
    }

    private JsonNode ChatSection()
    {
        lock (_gate)
        {
            return new JsonObject
            {
                ["channelId"] = _chatChannelId,
                ["channelName"] = _chatChannelName,
                ["direct"] = _chatIsDirect,
                ["messages"] = ToNode(_messages),
            };
        }
    }

    private static JsonNode? ToNode<T>(T? value) => JsonSerializer.SerializeToNode(value, JsonOptions);

    private void Emit(string section, JsonNode? data) => SectionChanged?.Invoke(section, data);
}
