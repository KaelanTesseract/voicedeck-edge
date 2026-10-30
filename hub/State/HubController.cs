using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using VoicedeckEdge.Hub.Discord;
using VoicedeckEdge.Hub.Infrastructure;

namespace VoicedeckEdge.Hub.State;

/// <summary>
/// Glue between Discord and the widgets. All Discord events and widget commands run one after another
/// on a single worker, so subscriptions and state never race.
/// </summary>
internal sealed class HubController
{
    private static readonly string[] GlobalEvents = ["VOICE_SETTINGS_UPDATE", "VOICE_CHANNEL_SELECT", "VOICE_CONNECTION_STATUS"];
    private static readonly string[] VoiceChannelEvents = ["VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE", "SPEAKING_START", "SPEAKING_STOP"];
    private static readonly string[] ChatEvents = ["MESSAGE_CREATE", "MESSAGE_UPDATE", "MESSAGE_DELETE"];
    private static readonly TimeSpan GuildCacheTime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ChannelCacheTime = TimeSpan.FromSeconds(30);
    private const int ParallelChannelLookups = 12;
    // Large servers: Discord may have to fetch uncached channels from the network. The widget waits 15 s,
    // so the list is returned after this deadline with whatever details arrived (the rest sorted by name).
    private static readonly TimeSpan ChannelListDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ChannelLookupTimeout = TimeSpan.FromSeconds(4);
    // Channels Discord never answers for (usually ones you cannot see): skipped from then on, so a server
    // with such channels does not wait for them on every visit. Memory only, cleared on reconnect.
    private readonly ConcurrentDictionary<string, bool> _silentChannels = new();

    private readonly StateStore _state;
    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    // Read-only lookups (guild/channel lists) run beside the worker, so these caches are concurrent.
    private readonly ConcurrentDictionary<string, string> _guildNames = new();
    private readonly ConcurrentDictionary<string, string> _channelGuild = new(); // "" = direct message
    private readonly ConcurrentDictionary<string, (DateTime At, JsonNode Data)> _channelCache = new();
    private volatile Tuple<DateTime, JsonNode>? _guildCache;
    private volatile DiscordIpc? _ipc;
    private string? _subscribedVoice;
    private string? _subscribedChat;

    public HubController(DiscordSession session, StateStore state)
    {
        _state = state;
        session.StatusChanged += s => _state.SetStatus(StatusName(s));
        session.Ready += (ipc, me) => Enqueue(() => InitAsync(ipc, me));
        session.Lost += () => Enqueue(() =>
        {
            _ipc = null;
            _subscribedVoice = _subscribedChat = null;
            _guildNames.Clear();
            _channelGuild.Clear();
            _silentChannels.Clear();
            _channelCache.Clear();
            _guildCache = null;
            _state.Reset();
            return Task.CompletedTask;
        });
        session.EventReceived += (evt, data) => Enqueue(() => HandleEventAsync(evt, data));
        _state.SetStatus(StatusName(session.Status));
    }

    /// <summary>How often the open chat is re-read for reactions (RPC has no reaction events).</summary>
    private static readonly TimeSpan ReactionRefresh = TimeSpan.FromSeconds(10);

    public async Task RunAsync(CancellationToken ct)
    {
        _ = Task.Run(() => ReactionLoopAsync(ct), ct);
        await foreach (var work in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await work();
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or DiscordRpcException)
            {
                Log.Warn($"Discord-Aufruf fehlgeschlagen: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error("Unerwarteter Fehler im Hub-Ablauf", ex);
            }
        }
    }

    /// <summary>Runs a validated widget command on the worker and returns its result data.</summary>
    public Task<JsonNode?> ExecuteAsync(WidgetCommand cmd)
    {
        // Page turning needs no Discord and must not wait behind voice commands.
        if (cmd.Op == "edge.page")
            return EdgePages.Turn(cmd.Next!.Value) ? Task.FromResult<JsonNode?>(null) : Task.FromException<JsonNode?>(new OpException("busy"));

        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Lists only read from Discord and can take a moment – they must not hold up speaking/voice events.
        Func<Task> work = async () =>
        {
            try
            {
                tcs.TrySetResult(cmd.Op switch
                {
                    "guilds.list" => await GuildsAsync(_ipc ?? throw new OpException("not_ready")),
                    "channels.list" => await ChannelsAsync(_ipc ?? throw new OpException("not_ready"), cmd.GuildId!),
                    _ => await ExecuteOnWorkerAsync(cmd),
                });
            }
            catch (OpException ex)
            {
                tcs.TrySetException(ex);
            }
            catch (DiscordRpcException ex)
            {
                Log.Warn($"{cmd.Op}: Discord-Fehler {ex.Code}");
                tcs.TrySetException(new OpException("discord_error"));
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                tcs.TrySetException(new OpException("not_ready"));
            }
        };
        if (cmd.Op is "guilds.list" or "channels.list") _ = Task.Run(work);
        else Enqueue(work);
        return tcs.Task;
    }

    private void Enqueue(Func<Task> work) => _queue.Writer.TryWrite(work);

    // ---------- Discord side ----------

    private async Task InitAsync(DiscordIpc ipc, JsonObject me)
    {
        _ipc = ipc;
        _subscribedVoice = _subscribedChat = null;
        _state.SetMe(me);
        foreach (var evt in GlobalEvents)
            await ipc.CommandAsync("SUBSCRIBE", evt: evt);
        try
        {
            await ipc.CommandAsync("SUBSCRIBE", evt: "NOTIFICATION_CREATE");
        }
        catch (DiscordRpcException ex)
        {
            Log.Warn($"Direktnachrichten nicht verfügbar (Discord-Fehler {ex.Code}).");
        }

        if (await ipc.CommandAsync("GET_VOICE_SETTINGS") is JsonObject settings)
            _state.ApplyVoiceSettings(settings);
        await SwitchVoiceAsync(await ipc.CommandAsync("GET_SELECTED_VOICE_CHANNEL") as JsonObject);
    }

    private async Task HandleEventAsync(string evt, JsonObject data)
    {
        switch (evt)
        {
            case "VOICE_STATE_CREATE" or "VOICE_STATE_UPDATE" or "VOICE_STATE_DELETE":
                _state.ApplyVoiceState(evt, data);
                break;
            case "SPEAKING_START" or "SPEAKING_STOP":
                if (data.Str("user_id") is { } userId) _state.SetSpeaking(userId, evt == "SPEAKING_START");
                break;
            case "VOICE_SETTINGS_UPDATE":
                _state.ApplyVoiceSettings(data);
                break;
            case "VOICE_CONNECTION_STATUS":
                if (data.Str("state") is { } connection) _state.SetConnection(connection);
                break;
            case "VOICE_CHANNEL_SELECT":
                var channelId = data.Str("channel_id");
                var channel = channelId == null || _ipc == null
                    ? null
                    : await _ipc.CommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = channelId }) as JsonObject;
                await SwitchVoiceAsync(channel);
                break;
            case "NOTIFICATION_CREATE":
                await ResolveNotificationGuildAsync(data);
                _state.ApplyNotification(data);
                break;
            case "MESSAGE_CREATE" or "MESSAGE_UPDATE" or "MESSAGE_DELETE":
                _state.ApplyMessage(evt, data);
                break;
        }
    }

    /// <summary>
    /// Server or DM? Notifications normally carry message.guild_id; when it is missing the channel is looked
    /// up once (GET_CHANNEL has guild_id) so a server message is never shown as a direct message.
    /// </summary>
    private async Task ResolveNotificationGuildAsync(JsonObject data)
    {
        if (data["message"] is not JsonObject message || message.Str("guild_id") != null || _ipc == null) return;
        if ((data.Str("channel_id") ?? message.Str("channel_id")) is not { } channelId || !Json.IsSnowflake(channelId)) return;
        if (!_channelGuild.TryGetValue(channelId, out var guildId))
        {
            try
            {
                var channel = await _ipc.CommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = channelId }) as JsonObject;
                guildId = channel.Str("guild_id") ?? "";
                _channelGuild[channelId] = guildId;
            }
            catch (DiscordRpcException)
            {
                return;
            }
        }
        if (guildId.Length > 0) message["guild_id"] = guildId;
    }

    private async Task SwitchVoiceAsync(JsonObject? channel)
    {
        var newId = channel.Str("id");
        if (_ipc != null && newId != _subscribedVoice)
        {
            await ResubscribeAsync(_ipc, VoiceChannelEvents, _subscribedVoice, newId);
            _subscribedVoice = newId;
        }
        _state.SetVoiceChannel(channel, await GuildNameAsync(channel.Str("guild_id")));
    }

    private async Task<string?> GuildNameAsync(string? guildId)
    {
        if (guildId == null || _ipc == null) return null;
        if (_guildNames.TryGetValue(guildId, out var name)) return name;
        try
        {
            name = (await _ipc.CommandAsync("GET_GUILD", new JsonObject { ["guild_id"] = guildId })).Str("name");
        }
        catch (DiscordRpcException)
        {
            name = null;
        }
        if (name != null) _guildNames[guildId] = name;
        return name;
    }

    /// <summary>Moves channel-bound subscriptions from one channel to another (either may be null).</summary>
    private static async Task ResubscribeAsync(DiscordIpc ipc, string[] events, string? oldChannel, string? newChannel)
    {
        foreach (var evt in events)
        {
            if (oldChannel != null)
            {
                try
                {
                    await ipc.CommandAsync("UNSUBSCRIBE", new JsonObject { ["channel_id"] = oldChannel }, evt);
                }
                catch (DiscordRpcException) { /* channel may be gone already */ }
            }
            if (newChannel != null)
                await ipc.CommandAsync("SUBSCRIBE", new JsonObject { ["channel_id"] = newChannel }, evt);
        }
    }

    // ---------- widget side ----------

    private async Task<JsonNode?> ExecuteOnWorkerAsync(WidgetCommand cmd)
    {
        var ipc = _ipc ?? throw new OpException("not_ready");
        switch (cmd.Op)
        {
            case "voice.toggleMute":
                return await SetVoiceSettingsAsync(ipc, new JsonObject { ["mute"] = !_state.Mute });
            case "voice.toggleDeaf":
                return await SetVoiceSettingsAsync(ipc, new JsonObject { ["deaf"] = !_state.Deaf });
            case "voice.setMode":
                return await SetVoiceSettingsAsync(ipc, new JsonObject { ["mode"] = new JsonObject { ["type"] = cmd.Mode } });

            case "audio.setInput" or "audio.setOutput":
                var input = cmd.Op == "audio.setInput";
                if (!_state.HasAudioDevice(input, cmd.DeviceId!)) throw new OpException("invalid_args");
                return await SetVoiceSettingsAsync(ipc, new JsonObject
                {
                    [input ? "input" : "output"] = new JsonObject { ["device_id"] = cmd.DeviceId },
                });
            case "audio.setInputVolume":
                return await SetVoiceSettingsAsync(ipc, new JsonObject { ["input"] = new JsonObject { ["volume"] = cmd.Volume } });
            case "audio.setOutputVolume":
                return await SetVoiceSettingsAsync(ipc, new JsonObject { ["output"] = new JsonObject { ["volume"] = cmd.Volume } });

            case "voice.leave":
                await ipc.CommandAsync("SELECT_VOICE_CHANNEL", new JsonObject { ["channel_id"] = null });
                return null;
            case "voice.join":
                await ipc.CommandAsync("SELECT_VOICE_CHANNEL",
                    new JsonObject { ["channel_id"] = cmd.ChannelId, ["force"] = true }, timeout: TimeSpan.FromSeconds(20));
                return null;

            case "user.setVolume":
                await SetUserAsync(ipc, cmd.UserId!, volume: cmd.Volume, mute: null);
                return null;
            case "user.setMute":
                await SetUserAsync(ipc, cmd.UserId!, volume: null, mute: cmd.Mute);
                return null;
            case "user.reset":
                await SetUserAsync(ipc, cmd.UserId!, volume: 100, mute: false);
                return null;

            case "chat.open":
                return await OpenChatAsync(ipc, cmd.ChannelId!);
            case "chat.close":
                await ResubscribeAsync(ipc, ChatEvents, _subscribedChat, null);
                _subscribedChat = null;
                _state.SetChat(null);
                return null;

            default:
                throw new OpException("unknown_op");
        }
    }

    private async Task<JsonNode?> SetVoiceSettingsAsync(DiscordIpc ipc, JsonObject args)
    {
        if (await ipc.CommandAsync("SET_VOICE_SETTINGS", args) is JsonObject result)
            _state.ApplyVoiceSettings(result);
        return null;
    }

    private async Task SetUserAsync(DiscordIpc ipc, string userId, int? volume, bool? mute)
    {
        var args = new JsonObject { ["user_id"] = userId };
        if (volume != null) args["volume"] = volume;
        if (mute != null) args["mute"] = mute;
        await ipc.CommandAsync("SET_USER_VOICE_SETTINGS", args);
        _state.UpdateMemberLocal(userId, volume, mute);
    }

    private async Task<JsonNode> GuildsAsync(DiscordIpc ipc)
    {
        if (_guildCache is { } cached && DateTime.UtcNow - cached.Item1 < GuildCacheTime) return cached.Item2.DeepClone();

        var data = await ipc.CommandAsync("GET_GUILDS");
        var guilds = new JsonArray();
        foreach (var g in data.Arr("guilds").OfType<JsonObject>())
        {
            if (g.Str("id") is not { } id) continue;
            var name = g.Str("name") ?? "";
            _guildNames[id] = name;
            guilds.Add(new JsonObject { ["id"] = id, ["name"] = name, ["icon"] = StateStore.IconPath(g.Str("icon_url")) });
        }
        var result = new JsonObject { ["guilds"] = guilds };
        _guildCache = Tuple.Create(DateTime.UtcNow, (JsonNode)result);
        return result.DeepClone();
    }

    /// <summary>
    /// GET_CHANNELS has no position, so every channel is looked up once with GET_CHANNEL
    /// (spike 2026-10-27). Categories cannot be resolved over RPC – voice and text are two groups.
    /// </summary>
    private async Task<JsonNode> ChannelsAsync(DiscordIpc ipc, string guildId)
    {
        if (_channelCache.TryGetValue(guildId, out var cached) && DateTime.UtcNow - cached.At < ChannelCacheTime)
            return cached.Data.DeepClone();

        var data = await ipc.CommandAsync("GET_CHANNELS", new JsonObject { ["guild_id"] = guildId });
        var wanted = data.Arr("channels").OfType<JsonObject>()
            .Where(ch => ch.Str("id") != null && ch.Int("type", -1) is 0 or 2 or 5 or 13) // text, voice, announcement, stage
            .ToList();

        // One pipe, many in-flight commands (matched by nonce): a few at a time instead of one by one.
        // A channel that fails or is too slow must not sink the whole list.
        var started = DateTime.UtcNow;
        using var deadline = new CancellationTokenSource(ChannelListDeadline);
        using var gate = new SemaphoreSlim(ParallelChannelLookups);
        var details = await Task.WhenAll(wanted.Select(async ch =>
        {
            if (_silentChannels.ContainsKey(ch.Str("id")!)) return (Channel: ch, Detail: (JsonNode?)null);
            try
            {
                await gate.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                return (Channel: ch, Detail: (JsonNode?)null);
            }
            try
            {
                var left = ChannelListDeadline - (DateTime.UtcNow - started);
                if (left <= TimeSpan.Zero) return (Channel: ch, Detail: (JsonNode?)null);
                var detail = await ipc.CommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = ch.Str("id") },
                    timeout: left < ChannelLookupTimeout ? left : ChannelLookupTimeout);
                return (Channel: ch, Detail: detail);
            }
            catch (TimeoutException)
            {
                // A lookup that ran into its own timeout (not the shared deadline) is a channel Discord never answers.
                if (DateTime.UtcNow - started < ChannelListDeadline) _silentChannels[ch.Str("id")!] = true;
                return (Channel: ch, Detail: (JsonNode?)null);
            }
            catch (DiscordRpcException)
            {
                _silentChannels[ch.Str("id")!] = true;
                return (Channel: ch, Detail: (JsonNode?)null);
            }
            finally
            {
                gate.Release();
            }
        }));

        var voice = new List<(int Pos, JsonObject Item)>();
        var text = new List<(int Pos, JsonObject Item)>();
        var missing = 0;
        foreach (var (ch, detail) in details)
        {
            if (detail == null && !_silentChannels.ContainsKey(ch.Str("id")!)) missing++; // only "ran out of time" counts as incomplete
            var pos = detail == null ? int.MaxValue : detail.Int("position"); // unknown position: after the known ones, by name
            var item = new JsonObject { ["id"] = ch.Str("id"), ["name"] = ch.Str("name") ?? "" };
            if (ch.Int("type") is 2 or 13)
            {
                item["members"] = detail.Arr("voice_states").Count;
                voice.Add((pos, item));
            }
            else
            {
                text.Add((pos, item));
            }
        }
        Log.Info($"Kanalliste: {details.Length} Kanäle in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms, " +
                 $"{missing} noch ohne Details, {details.Count(d => d.Detail == null) - missing} ohne Antwort von Discord.");

        static JsonArray Sorted(List<(int Pos, JsonObject Item)> list) =>
            new(list.OrderBy(c => c.Pos).ThenBy(c => c.Item.Str("name"), StringComparer.CurrentCultureIgnoreCase)
                .Select(c => (JsonNode?)c.Item).ToArray());

        var result = new JsonObject { ["guildId"] = guildId, ["voice"] = Sorted(voice), ["text"] = Sorted(text) };
        if (missing == 0) _channelCache[guildId] = (DateTime.UtcNow, result); // incomplete lists are asked again next time
        return result.DeepClone();
    }

    private async Task ReactionLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ReactionRefresh);
        while (await timer.WaitForNextTickAsync(ct))
        {
            // On the worker, so it never races with opening or closing a chat.
            Enqueue(async () =>
            {
                if (_ipc is not { } ipc || _subscribedChat is not { } channelId) return;
                if (await ipc.CommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = channelId }) is JsonObject channel)
                    _state.RefreshReactions(channelId, channel);
            });
        }
    }

    private async Task<JsonNode?> OpenChatAsync(DiscordIpc ipc, string channelId)
    {
        var channel = await ipc.CommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = channelId }) as JsonObject;
        if (channel.Int("type", -1) is not (0 or 5 or 1 or 3)) throw new OpException("invalid_args"); // text, announcement, DM, group DM

        if (_subscribedChat != channelId)
        {
            await ResubscribeAsync(ipc, ChatEvents, _subscribedChat, channelId);
            _subscribedChat = channelId;
        }
        _state.SetChat(channel);
        return null;
    }

    private static string StatusName(DiscordStatus status) => status switch
    {
        DiscordStatus.NotConfigured => "not_configured",
        DiscordStatus.InvalidClientId => "invalid_client_id",
        DiscordStatus.DiscordNotRunning => "discord_not_running",
        DiscordStatus.Connecting => "connecting",
        DiscordStatus.Unauthorized => "unauthorized",
        DiscordStatus.Authorizing => "authorizing",
        DiscordStatus.Ready => "ready",
        _ => "unknown",
    };
}
