using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TarkovPriceOverlay.Interop;

public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private static int _nextId = 0x544B;
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private HwndSource? _source;
    private IntPtr _handle;
    public event EventHandler? Pressed;

    public void Register(Window window, string gestureText)
    {
        Unregister();
        var gesture = Parse(gestureText);
        var modifiers = ToNativeModifiers(gesture.Modifiers);
        var key = KeyInterop.VirtualKeyFromKey(gesture.Key);
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle)
            ?? throw new InvalidOperationException("无法取得主窗口句柄，快捷键尚未启用。");

        // MOD_NOREPEAT avoids a held key starting several scans. A few keyboard
        // drivers reject that flag, so retry once without it before reporting a conflict.
        if (!RegisterHotKey(_handle, _id, modifiers | ModNoRepeat, (uint)key) &&
            !RegisterHotKey(_handle, _id, modifiers, (uint)key))
        {
            var error = Marshal.GetLastWin32Error();
            _source = null;
            _handle = IntPtr.Zero;
            throw new InvalidOperationException(
                $"无法启用快捷键 {Normalize(gestureText)}（系统错误 {error}）。请关闭其他 LootPilot 或更换按键。");
        }

        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _id)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(ModifierKeys keys)
    {
        uint value = 0;
        if (keys.HasFlag(ModifierKeys.Alt)) value |= 0x0001;
        if (keys.HasFlag(ModifierKeys.Control)) value |= 0x0002;
        if (keys.HasFlag(ModifierKeys.Shift)) value |= 0x0004;
        if (keys.HasFlag(ModifierKeys.Windows)) value |= 0x0008;
        return value;
    }

    public static ParsedHotkey Parse(string gestureText)
    {
        if (string.IsNullOrWhiteSpace(gestureText))
            throw new InvalidOperationException("快捷键不能为空。");

        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var rawPart in gestureText.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();
            switch (part.ToLowerInvariant())
            {
                case "alt": modifiers |= ModifierKeys.Alt; continue;
                case "ctrl":
                case "control": modifiers |= ModifierKeys.Control; continue;
                case "shift": modifiers |= ModifierKeys.Shift; continue;
                case "win":
                case "windows": modifiers |= ModifierKeys.Windows; continue;
            }

            if (key is not null)
                throw new InvalidOperationException($"快捷键格式无效：{gestureText}");
            key = ParseKey(part);
        }

        if (key is null || key is Key.None)
            throw new InvalidOperationException($"快捷键格式无效：{gestureText}");
        return new ParsedHotkey(key.Value, modifiers);
    }

    public static string Normalize(string gestureText) => Format(Parse(gestureText));

    public static string Format(ParsedHotkey hotkey)
    {
        var parts = new List<string>(5);
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(hotkey.Key.ToString());
        return string.Join('+', parts);
    }

    private static Key ParseKey(string text)
    {
        var normalized = text.Trim().Replace(" ", "");
        // TextBox normally turns a NumPad press into the character "8"/"9".
        // Treat a bare digit as NumPad so existing user settings keep working.
        if (normalized.Length == 1 && char.IsDigit(normalized[0]))
            normalized = "NumPad" + normalized;
        if (normalized.StartsWith("小键盘", StringComparison.OrdinalIgnoreCase))
            normalized = "NumPad" + normalized[3..];
        if (normalized.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) &&
            normalized.Length == 4 && char.IsDigit(normalized[3]))
            normalized = "NumPad" + normalized[3];

        normalized = normalized.ToLowerInvariant() switch
        {
            "num+" or "numpad+" or "numpadadd" => "Add",
            "num-" or "numpad-" or "numpadsubtract" => "Subtract",
            "num*" or "numpad*" or "numpadmultiply" => "Multiply",
            "num/" or "numpad/" or "numpaddivide" => "Divide",
            "num." or "numpad." or "numpaddecimal" => "Decimal",
            _ => normalized
        };

        if (Enum.TryParse<Key>(normalized, ignoreCase: true, out var parsed)) return parsed;
        throw new InvalidOperationException($"不支持的按键：{text}");
    }

    public readonly record struct ParsedHotkey(Key Key, ModifierKeys Modifiers);

    public void Dispose()
    {
        Unregister();
    }

    public void Unregister()
    {
        if (_handle != IntPtr.Zero) UnregisterHotKey(_handle, _id);
        _source?.RemoveHook(WndProc);
        _source = null;
        _handle = IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
