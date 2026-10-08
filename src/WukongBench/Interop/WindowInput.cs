using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WukongBench.Interop;

internal static class WindowInput
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { public ushort Key, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static void Activate(nint window)
    {
        if (GetForegroundWindow() == window) return;
        ShowWindow(window, 9);
        SetForegroundWindow(window);
    }
    public static void Key(nint window, ushort key)
    {
        EnsureForeground(window);
        Send([new() { Type = 1, Data = new() { Keyboard = new() { Key = key } } },
            new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = 2 } } }]);
    }
    public static void Click(nint window, int x, int y)
    {
        EnsureForeground(window);
        if (!GetClientRect(window, out var rect) || x < 0 || y < 0 || x >= rect.Right || y >= rect.Bottom)
            throw new InvalidOperationException("Кнопка вне клиентской области окна.");
        var point = new Point { X = x, Y = y };
        if (!ClientToScreen(window, ref point)) throw new Win32Exception();
        if (GetAncestor(WindowFromPoint(point), 2) != window) throw new InvalidOperationException("Окно бенчмарка перекрыто другим окном.");
        if (!SetCursorPos(point.X, point.Y)) throw new Win32Exception();
        EnsureForeground(window);
        Send([new() { Data = new() { Mouse = new() { Flags = 2 } } }, new() { Data = new() { Mouse = new() { Flags = 4 } } }]);
    }
    public static void EnsureForeground(nint window)
    {
        if (GetForegroundWindow() != window) throw new InvalidOperationException("Не удалось активировать окно Benchmark Tool. Закройте перекрывающие окна и запустите из интерактивного сеанса.");
    }
    private static void Send(Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput не отправил ввод. Проверьте одинаковый уровень прав программы и бенчмарка.");
    }
}
