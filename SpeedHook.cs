using System.Globalization;
using System.Runtime.InteropServices;
using Doorstop;

namespace MiniSpeed;

internal static class SpeedHook
{
    [DllImport("user32")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    internal static readonly object SpeedLock = new();
    private static bool toggleHeld, reloadHeld;
    private static readonly uint ProcessId = (uint)Environment.ProcessId;

    internal static void StartKeyMonitor() =>
        new Thread(MonitorKeys) { IsBackground = true, Name = "MiniSpeed key monitor" }.Start();

    private static void MonitorKeys()
    {
        Entrypoint.Log("F6/F7 key monitor started; keys are checked whenever the game has focus, independently of speed queries.");
        try
        {
            while (true)
            {
                lock (SpeedLock)
                {
                    PollKeys();
                }
                Thread.Sleep(15);
            }
        }
        catch (Exception ex) { Entrypoint.Log($"F6/F7 key monitor failed: {ex}"); }
    }

    private static void PollKeys()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var foreground);
        if (foreground != ProcessId) { toggleHeld = reloadHeld = false; return; }
        bool toggle = (GetAsyncKeyState(0x75) & 0x8000) != 0;
        bool reload = (GetAsyncKeyState(0x76) & 0x8000) != 0;
        if (toggle && !toggleHeld)
        {
            Entrypoint.Log("F6 pressed: toggling movement speed modification.");
            Settings.Enabled = !Settings.Enabled;
            ReportSettingsChange("F6");
        }
        if (reload && !reloadHeld)
        {
            Entrypoint.Log("F7 pressed: reloading MiniSpeed.cfg.");
            Settings.Reload();
            ReportSettingsChange("F7");
        }
        toggleHeld = toggle;
        reloadHeld = reload;
    }

    private static void ReportSettingsChange(string key)
    {
        Entrypoint.Log($"{key}: base-speed setting now Enabled={Settings.Enabled}, SpeedMultiplier={Settings.Multiplier.ToString(CultureInfo.InvariantCulture)}. Takes effect on the next hero base-speed calculation or movement attribute refresh; this can wait until a scene or speed bonus change.");
    }
}
