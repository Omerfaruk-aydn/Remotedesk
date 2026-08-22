using System.Runtime.InteropServices;

namespace SecureRemoteDesk.Desktop.Services;

internal static class Win32Input
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
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

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFRightDown = 0x0008;
    private const uint MouseEventFRightUp = 0x0010;
    private const uint MouseEventFWheel = 0x0800;
    private const uint KeyEventFKeyUp = 0x0002;

    public static void MoveTo(int x, int y) => SetCursorPos(x, y);

    public static void MouseButton(bool left, bool down)
    {
        var flags = left
            ? (down ? MouseEventFLeftDown : MouseEventFLeftUp)
            : (down ? MouseEventFRightDown : MouseEventFRightUp);
        var input = new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } } };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public static void MouseWheel(int delta)
    {
        var input = new INPUT
        {
            type = InputMouse,
            U = new InputUnion { mi = new MOUSEINPUT { mouseData = unchecked((uint)delta), dwFlags = MouseEventFWheel } }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public static void Key(int vk, bool down)
    {
        var input = new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = down ? 0u : KeyEventFKeyUp } }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }
}
