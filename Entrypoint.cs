using MiniSpeed;

namespace Doorstop;

public static class Entrypoint
{
    internal static readonly string Root = Path.GetDirectoryName(Environment.ProcessPath)!;
    private static readonly object LogLock = new();

    internal static void Log(string text)
    {
        lock (LogLock)
        {
            try { File.AppendAllText(Path.Combine(Root, "MiniSpeed.log"), $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}"); }
            catch (IOException) { /* Logging must never interrupt the game. */ }
        }
    }

    public static void Start() =>
        new Thread(Run) { IsBackground = true, Name = "MiniSpeed initialization" }.Start();

    private static void Run()
    {
        try
        {
            Log("MiniSpeed 0.3.0 starting (character base-speed build).");
            Settings.Reload();
            SpeedHook.StartKeyMonitor();
            UpgradeHook.Initialize();
        }
        catch (Exception ex) { Log($"Initialization failed: {ex}"); }
    }
}
