using System.Text.Json.Nodes;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Tests;

public class AudioSettingsTests
{
    // Shape of GET_VOICE_SETTINGS (input/output part).
    private static JsonObject Settings() => new()
    {
        ["mute"] = false,
        ["deaf"] = false,
        ["mode"] = new JsonObject { ["type"] = "VOICE_ACTIVITY" },
        ["input"] = new JsonObject
        {
            ["device_id"] = "default",
            ["volume"] = 80,
            ["available_devices"] = new JsonArray
            {
                new JsonObject { ["id"] = "default", ["name"] = "Default" },
                new JsonObject { ["id"] = "{0.0.1.00000000}.{mic-1}", ["name"] = "Headset Microphone" },
            },
        },
        ["output"] = new JsonObject
        {
            ["device_id"] = "{0.0.0.00000000}.{spk-1}",
            ["volume"] = 150,
            ["available_devices"] = new JsonArray { new JsonObject { ["id"] = "{0.0.0.00000000}.{spk-1}", ["name"] = "Speakers" } },
        },
    };

    [Fact]
    public void Devices_and_volumes_reach_the_widget()
    {
        var store = new StateStore();
        store.ApplyVoiceSettings(Settings());
        var input = store.Snapshot()["settings"]!["input"]!;

        Assert.Equal("default", input["deviceId"]!.GetValue<string>());
        Assert.Equal(80, input["volume"]!.GetValue<int>());
        Assert.Equal("Headset Microphone", input["devices"]![1]!["name"]!.GetValue<string>());
        Assert.Equal(150, store.Snapshot()["settings"]!["output"]!["volume"]!.GetValue<int>());
    }

    [Fact]
    public void Partial_updates_keep_the_device_lists()
    {
        var store = new StateStore();
        store.ApplyVoiceSettings(Settings());
        store.ApplyVoiceSettings(new JsonObject { ["input"] = new JsonObject { ["volume"] = 40 } });

        var input = store.Snapshot()["settings"]!["input"]!;
        Assert.Equal(40, input["volume"]!.GetValue<int>());
        Assert.Equal(2, input["devices"]!.AsArray().Count);
        Assert.True(store.HasAudioDevice(input: true, "{0.0.1.00000000}.{mic-1}"));
        Assert.False(store.HasAudioDevice(input: false, "{0.0.1.00000000}.{mic-1}")); // a microphone is no output
    }

    [Fact]
    public void Device_commands_are_validated()
    {
        static WidgetCommand Parse(string json) => WidgetCommand.Parse(JsonNode.Parse(json)!.AsObject());
        Assert.Equal("default", Parse("""{"op":"audio.setInput","deviceId":"default"}""").DeviceId);
        Assert.Equal(100, Parse("""{"op":"audio.setInputVolume","volume":180}""").Volume);   // input tops out at 100
        Assert.Equal(180, Parse("""{"op":"audio.setOutputVolume","volume":180}""").Volume);
        Assert.Equal("invalid_args", Assert.Throws<OpException>(() => Parse("""{"op":"audio.setOutput"}""")).Code);
        Assert.Equal("invalid_args", Assert.Throws<OpException>(() => Parse("""{"op":"audio.setOutput","deviceId":"a\nb"}""")).Code);
    }
}
