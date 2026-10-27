using System.Runtime.InteropServices;

namespace VoicedeckEdge.Hub.Infrastructure;

/// <summary>
/// Turns XENEON EDGE pages for the widget. iCUE gives touch widgets every swipe, so the widget detects the
/// swipe itself and the hub presses the shortcut the user assigned in iCUE
/// (device settings → "Previous/Next display screen"): Ctrl+Alt+Shift+Left / Right.
/// </summary>
internal static class EdgePages
{
    private const ushort VkLeft = 0x25, VkRight = 0x27, VkLShift = 0xA0, VkLControl = 0xA2, VkLMenu = 0xA4;
    private const uint InputKeyboard = 1, KeyEventFExtendedKey = 0x1, KeyEventFKeyUp = 0x2;
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(350);
    private static long _lastTicks;

    /// <summary>Presses the page shortcut. Returns false when throttled or when Windows refused the input.</summary>
    public static bool Turn(bool next)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastTicks);
        if (now - last < MinInterval.TotalMilliseconds || Interlocked.CompareExchange(ref _lastTicks, now, last) != last)
            return false;

        var arrow = next ? VkRight : VkLeft;
        INPUT[] seq =
        [
            Key(VkLControl, up: false), Key(VkLMenu, up: false), Key(VkLShift, up: false), Key(arrow, up: false, extended: true),
            Key(arrow, up: true, extended: true), Key(VkLShift, up: true), Key(VkLMenu, up: true), Key(VkLControl, up: true),
        ];
        var sent = SendInput((uint)seq.Length, seq, Marshal.SizeOf<INPUT>());
        if (sent != seq.Length) Log.Warn($"Seitenwechsel: Windows hat den Tastendruck abgelehnt ({Marshal.GetLastWin32Error()})");
        return sent == seq.Length;
    }

    private static INPUT Key(ushort vk, bool up, bool extended = false) => new()
    {
        Type = InputKeyboard,
        Ki = new KEYBDINPUT
        {
            Vk = vk,
            Scan = (ushort)MapVirtualKey(vk, 0),
            Flags = (up ? KeyEventFKeyUp : 0) | (extended ? KeyEventFExtendedKey : 0),
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT is a union; the keyboard variant sits after the 4-byte type (8 on x64 due to alignment).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KEYBDINPUT Ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
