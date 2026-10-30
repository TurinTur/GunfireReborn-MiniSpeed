using System.Runtime.InteropServices;

namespace MiniSpeed;

internal static class NativeHooks
{
    [DllImport("MinHook.x64", CallingConvention = CallingConvention.Winapi)]
    private static extern int MH_Initialize();
    [DllImport("MinHook.x64", CallingConvention = CallingConvention.Winapi)]
    private static extern int MH_CreateHook(nint target, nint detour, out nint original);
    [DllImport("MinHook.x64", CallingConvention = CallingConvention.Winapi)]
    private static extern int MH_EnableHook(nint target);

    internal static nint Create(nint target, nint detour)
    {
        int status = MH_Initialize();
        if (status != 0 && status != 1) Check(status, "initialize MinHook");
        Check(MH_CreateHook(target, detour, out var trampoline), "create hook");
        return trampoline;
    }

    internal static void Enable(nint target) => Check(MH_EnableHook(target), "enable hook");

    private static void Check(int status, string action)
    {
        if (status != 0) throw new InvalidOperationException($"Cannot {action}: MinHook status {status}");
    }
}
