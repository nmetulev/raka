using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Raka.DevTools.Core;

/// <summary>
/// Provides Win32 SendInput-based input simulation for real keystroke and mouse click injection.
/// </summary>
internal static class InputSimulator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    private const int INPUT_KEYBOARD = 1;
    private const int INPUT_MOUSE = 0;
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>
    /// Sends text as real keystrokes using Unicode input events.
    /// Must be called from a background thread so the UI thread can process the messages.
    /// </summary>
    public static async Task SendKeysAsync(string text, int interKeyDelayMs = 30)
    {
        foreach (char c in text)
        {
            SendUnicodeChar(c);
            if (interKeyDelayMs > 0)
                await Task.Delay(interKeyDelayMs);
        }
    }

    private static void SendUnicodeChar(char c)
    {
        var inputs = new INPUT[2];

        // Key down
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = 0;
        inputs[0].u.ki.wScan = c;
        inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;

        // Key up
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = 0;
        inputs[1].u.ki.wScan = c;
        inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Sends a mouse click at the specified screen coordinates.
    /// Coordinates are in screen pixels (not normalized).
    /// </summary>
    public static void SendClick(int screenX, int screenY)
    {
        // Convert screen coords to normalized absolute coords (0-65535)
        int primaryScreenWidth = GetSystemMetrics(SM_CXSCREEN);
        int primaryScreenHeight = GetSystemMetrics(SM_CYSCREEN);
        int normalizedX = (int)((screenX * 65535.0) / primaryScreenWidth);
        int normalizedY = (int)((screenY * 65535.0) / primaryScreenHeight);

        var inputs = new INPUT[3];

        // Move to position
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dx = normalizedX;
        inputs[0].u.mi.dy = normalizedY;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;

        // Mouse down
        inputs[1].type = INPUT_MOUSE;
        inputs[1].u.mi.dx = normalizedX;
        inputs[1].u.mi.dy = normalizedY;
        inputs[1].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_ABSOLUTE;

        // Mouse up
        inputs[2].type = INPUT_MOUSE;
        inputs[2].u.mi.dx = normalizedX;
        inputs[2].u.mi.dy = normalizedY;
        inputs[2].u.mi.dwFlags = MOUSEEVENTF_LEFTUP | MOUSEEVENTF_ABSOLUTE;

        SendInput(3, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Sends a keyboard shortcut (e.g., Ctrl+S, Alt+F4).
    /// </summary>
    public static void SendHotkey(ushort[] modifierVirtualKeys, ushort mainVirtualKey)
    {
        int count = modifierVirtualKeys.Length * 2 + 2; // down+up for each modifier + main key
        var inputs = new INPUT[count];
        int idx = 0;

        // Press modifiers
        foreach (var vk in modifierVirtualKeys)
        {
            inputs[idx].type = INPUT_KEYBOARD;
            inputs[idx].u.ki.wVk = vk;
            inputs[idx].u.ki.wScan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC);
            inputs[idx].u.ki.dwFlags = KEYEVENTF_KEYDOWN;
            idx++;
        }

        // Press main key
        inputs[idx].type = INPUT_KEYBOARD;
        inputs[idx].u.ki.wVk = mainVirtualKey;
        inputs[idx].u.ki.wScan = (ushort)MapVirtualKeyW(mainVirtualKey, MAPVK_VK_TO_VSC);
        inputs[idx].u.ki.dwFlags = KEYEVENTF_KEYDOWN;
        idx++;

        // Release main key
        inputs[idx].type = INPUT_KEYBOARD;
        inputs[idx].u.ki.wVk = mainVirtualKey;
        inputs[idx].u.ki.wScan = (ushort)MapVirtualKeyW(mainVirtualKey, MAPVK_VK_TO_VSC);
        inputs[idx].u.ki.dwFlags = KEYEVENTF_KEYUP;
        idx++;

        // Release modifiers in reverse order
        for (int i = modifierVirtualKeys.Length - 1; i >= 0; i--)
        {
            inputs[idx].type = INPUT_KEYBOARD;
            inputs[idx].u.ki.wVk = modifierVirtualKeys[i];
            inputs[idx].u.ki.wScan = (ushort)MapVirtualKeyW(modifierVirtualKeys[i], MAPVK_VK_TO_VSC);
            inputs[idx].u.ki.dwFlags = KEYEVENTF_KEYUP;
            idx++;
        }

        SendInput((uint)count, inputs, Marshal.SizeOf<INPUT>());
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
