using System.Diagnostics;
using System.Runtime.InteropServices;
using Doorstop;

namespace MiniSpeed;

internal static class SpeedHook
{
    [DllImport("MinHook.x64", CallingConvention = CallingConvention.Winapi)]
    private static extern int MH_Initialize();
    [DllImport("MinHook.x64", CallingConvention = CallingConvention.Winapi)]
    private static extern int MH_CreateHook(nint target, nint detour, out nint original);
    [DllImport("MinHook.x64", CallingConvention = CallingConvention.Winapi)]
    private static extern int MH_EnableHook(nint target);
    [DllImport("user32")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SpeedGetter(nint instance, nint method);

    // Keep both delegates rooted for the entire process lifetime.
    private static readonly SpeedGetter Detour = GetSpeed;
    private static SpeedGetter original = null!;
    private static NativeApi.StaticValue staticValue = null!;
    private static NativeApi.FieldValue fieldValue = null!;
    private static nint heroField, propField;
    private static bool toggleHeld, reloadHeld, reportedError;
    private static nint lastProp;
    private static long nextKeyCheck;
    private static readonly uint ProcessId = (uint)Environment.ProcessId;

    internal static void Install(nint image)
    {
        var klass = NativeApi.Export<NativeApi.ClassFromName>("il2cpp_class_from_name");
        var field = NativeApi.Export<NativeApi.NamedMember>("il2cpp_class_get_field_from_name");
        var method = NativeApi.Export<NativeApi.Method>("il2cpp_class_get_method_from_name");
        var prop = klass(image, "", "PlayerProp");
        var motion = klass(image, "", "CharacterMotionManager");
        var player = klass(image, "", "NewPlayerObject");
        if (prop == 0 || motion == 0 || player == 0)
            throw new InvalidOperationException("Required player classes are missing in this game version.");
        heroField = field(motion, "HeroObj");
        propField = field(player, "playerProp");
        var getter = method(prop, "get_Speed", 0);
        if (heroField == 0 || propField == 0 || getter == 0)
            throw new InvalidOperationException("Required player fields or speed getter are missing.");
        var returnType = NativeApi.Export<NativeApi.OnePtr>("il2cpp_method_get_return_type")(getter);
        var typeName = NativeApi.Text(NativeApi.Export<NativeApi.OnePtr>("il2cpp_type_get_name")(returnType));
        if (typeName != "System.Int32") throw new InvalidOperationException($"Unsupported speed type: {typeName}");
        staticValue = NativeApi.Export<NativeApi.StaticValue>("il2cpp_field_static_get_value");
        fieldValue = NativeApi.Export<NativeApi.FieldValue>("il2cpp_field_get_value");
        var target = Marshal.ReadIntPtr(getter);
        if (target == IntPtr.Zero) throw new InvalidOperationException("Speed getter has no native address.");
        Check(MH_Initialize(), "initialize MinHook");
        Check(MH_CreateHook(target, Marshal.GetFunctionPointerForDelegate(Detour), out var trampoline), "create speed hook");
        original = Marshal.GetDelegateForFunctionPointer<SpeedGetter>(trampoline);
        Check(MH_EnableHook(target), "enable speed hook");
        Entrypoint.Log("PlayerProp.get_Speed hook installed. Waiting for the local hero. F6 toggles; F7 reloads MiniSpeed.cfg.");
    }

    private static void Check(int status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"Cannot {operation}: MinHook status {status}");
    }

    private static int GetSpeed(nint instance, nint method)
    {
        int speed = original(instance, method);
        try
        {
            // Read only: leave the underlying stat untouched, avoiding compounding.
            staticValue(heroField, out var hero);
            if (hero == 0 || instance == 0) return speed;
            fieldValue(hero, propField, out var localProp);
            if (instance != localProp) return speed;
            PollKeys();
            var result = Settings.Enabled ? (int)Math.Clamp(Math.Round(speed * (double)Settings.Multiplier), int.MinValue, int.MaxValue) : speed;
            if (lastProp != localProp)
            {
                lastProp = localProp;
                Entrypoint.Log($"Local hero speed hook active: original={speed}, modified={result}.");
            }
            return result;
        }
        catch (Exception ex)
        {
            if (!reportedError) { reportedError = true; Entrypoint.Log($"Speed hook error; returning original speed: {ex}"); }
            return speed;
        }
    }

    private static void PollKeys()
    {
        long now = Stopwatch.GetTimestamp();
        if (now < nextKeyCheck) return;
        nextKeyCheck = now + Stopwatch.Frequency / 30;
        GetWindowThreadProcessId(GetForegroundWindow(), out var foreground);
        if (foreground != ProcessId) { toggleHeld = reloadHeld = false; return; }
        bool toggle = (GetAsyncKeyState(0x75) & 0x8000) != 0;
        bool reload = (GetAsyncKeyState(0x76) & 0x8000) != 0;
        if (toggle && !toggleHeld)
        {
            Settings.Enabled = !Settings.Enabled;
            Entrypoint.Log($"Speed mod {(Settings.Enabled ? "enabled" : "disabled")}.");
        }
        if (reload && !reloadHeld) Settings.Reload();
        toggleHeld = toggle;
        reloadHeld = reload;
    }
}
