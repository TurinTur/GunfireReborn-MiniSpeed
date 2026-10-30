using System.Runtime.InteropServices;
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
            Log("MiniSpeed 0.1.0 starting.");
            Settings.Reload();
            while (NativeApi.GetModuleHandle("GameAssembly.dll") == 0) Thread.Sleep(100);
            var domainGet = NativeApi.Export<NativeApi.NoArgs>("il2cpp_domain_get");
            var assemblies = NativeApi.Export<NativeApi.Assemblies>("il2cpp_domain_get_assemblies");
            nint domain, list;
            do
            {
                Thread.Sleep(250);
                domain = domainGet();
                list = domain == 0 ? 0 : assemblies(domain, out _);
            } while (list == 0);
            var thread = NativeApi.Export<NativeApi.OnePtr>("il2cpp_thread_attach")(domain);
            try
            {
                list = assemblies(domain, out var count);
                var imageGet = NativeApi.Export<NativeApi.OnePtr>("il2cpp_assembly_get_image");
                var imageName = NativeApi.Export<NativeApi.OnePtr>("il2cpp_image_get_name");
                for (int i = 0; i < (int)count; i++)
                {
                    var image = imageGet(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                    if (NativeApi.Text(imageName(image)) != "Assembly-CSharp.dll") continue;
                    SpeedHook.Install(image);
                    return;
                }
                throw new InvalidOperationException("Assembly-CSharp.dll not found.");
            }
            finally { NativeApi.Export<Detach>("il2cpp_thread_detach")(thread); }
        }
        catch (Exception ex) { Log($"Initialization failed: {ex}"); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Detach(nint thread);
}
