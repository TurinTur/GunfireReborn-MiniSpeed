using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Doorstop;

namespace MiniSpeed;

internal static class UpgradeHook
{
    // These private CPython entry points are validated against this exact DLL.
    // A changed gameplay DLL disables this feature rather than using stale addresses.
    private const string SupportedHash = "F30326B86CFF21AECF58D7D1F6CE4593122DC49E7F3139DEA65F76E1F222698E";
    private const int EvalRva = 0x439f20, ThreadStateRva = 0xe484e8;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint Evaluate(nint frame, int throwFlag);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint Allocate(nint type, nint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint Call(nint callable, nint args, nint kwargs);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Destroy(nint value);
    [DllImport("kernel32", SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nuint size, uint allocation, uint protect);

    private static readonly Evaluate Detour = OnEvaluate;
    private static Evaluate original = null!;
    private static nint module, filter, targetCode;
    private static Allocate allocateInt = null!;
    private static nint intType;
    private static long nextDiscovery;
    private static int reportedLevel = -1;
    private static bool failed;

    internal static void Initialize()
    {
        try
        {
            var path = Path.Combine(Entrypoint.Root, "Gunfire Reborn_Data", "Plugins", "m1logic4.dll");
            using var sha = SHA256.Create();
            using (var stream = File.OpenRead(path))
                if (Convert.ToHexString(sha.ComputeHash(stream)) != SupportedHash)
                {
                    Entrypoint.Log("Weapon upgrade hook disabled: this gameplay DLL version is not supported. Movement mod remains available.");
                    return;
                }
            var deadline = DateTime.UtcNow.AddMinutes(2);
            do
            {
                module = NativeApi.GetModuleHandle("m1logic4.dll");
                if (module != 0 && Marshal.ReadIntPtr(module + ThreadStateRva) != IntPtr.Zero) break;
                Thread.Sleep(100);
            } while (DateTime.UtcNow < deadline);
            if (module == 0 || Marshal.ReadIntPtr(module + ThreadStateRva) == IntPtr.Zero)
                throw new InvalidOperationException("Embedded Python did not initialize.");

            // Native fast path: unrelated Python frames jump straight to the
            // original evaluator, without entering managed code after discovery.
            byte[] code = {
                0x48,0x8b,0x41,0x20,             // mov rax,[rcx+32] (frame.f_code)
                0x49,0xbb,0,0,0,0,0,0,0,0,      // mov r11,targetCode
                0x4d,0x85,0xdb,                 // test r11,r11
                0x74,0x05,                      // je managed (discover target)
                0x4c,0x39,0xd8,                 // cmp rax,r11
                0x75,0x0c,                      // jne original
                0x48,0xb8,0,0,0,0,0,0,0,0,0xff,0xe0,
                0x48,0xb8,0,0,0,0,0,0,0,0,0xff,0xe0
            };
            filter = VirtualAlloc(0, (nuint)code.Length, 0x3000, 0x40);
            if (filter == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var threadState = Marshal.ReadIntPtr(module + ThreadStateRva);
            var interpreter = Marshal.ReadIntPtr(threadState, 16);
            var evaluator = Marshal.ReadIntPtr(interpreter, 104);
            if (evaluator != module + EvalRva) throw new InvalidOperationException("Unexpected Python frame evaluator.");
            original = Marshal.GetDelegateForFunctionPointer<Evaluate>(evaluator);
            BitConverter.GetBytes((long)Marshal.GetFunctionPointerForDelegate(Detour)).CopyTo(code, 26);
            BitConverter.GetBytes((long)evaluator).CopyTo(code, 38);
            Marshal.Copy(code, 0, filter, code.Length);
            // CPython 3.6 exposes a per-interpreter frame-evaluation callback.
            // Replace that callback rather than patching protected native code.
            Marshal.WriteIntPtr(interpreter, 104, filter);
            Entrypoint.Log("Weapon upgrade evaluator hook installed; waiting for cl_wardata.CWarData.");
        }
        catch (Exception ex) { Entrypoint.Log($"Weapon upgrade initialization failed: {ex}"); }
    }

    private static nint OnEvaluate(nint frame, int throwFlag)
    {
        if (!failed)
        {
            try
            {
                if (targetCode == 0)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (now >= nextDiscovery)
                    {
                        nextDiscovery = now + Stopwatch.Frequency;
                        Discover(); // Called with the Python GIL held.
                    }
                }
                var pricing = Settings.Upgrades;
                if (targetCode != 0 && pricing.Enabled && throwFlag == 0 && Marshal.ReadIntPtr(frame, 32) == targetCode)
                {
                    // This game's Python build extends frame bookkeeping. Derive
                    // fast-local addresses from f_valuestack and co_nlocals rather
                    // than assuming the stock frame object's total size.
                    int localCount = Marshal.ReadInt32(targetCode, 24);
                    var locals = Marshal.ReadIntPtr(frame, 64) - localCount * 8;
                    var levelObject = Marshal.ReadIntPtr(locals, 8);
                    if (Marshal.ReadIntPtr(levelObject, 8) == intType)
                    {
                        long digits = Marshal.ReadInt64(levelObject, 16);
                        if (digits is 0 or 1)
                        {
                            int level = digits == 0 ? 0 : Marshal.ReadInt32(levelObject, 24);
                            if (level >= 0)
                            {
                                int cost = pricing.Cost(level);
                                var result = allocateInt(intType, 1);
                                if (result == 0) return 0; // Preserve Python's allocation error.
                                // CPython integers use base-2^30 digits. Our costs saturate
                                // at Int32.MaxValue, so allocate a second digit if needed.
                                if (cost >= 1 << 30)
                                {
                                    Release(result);
                                    result = allocateInt(intType, 2);
                                    if (result == 0) return 0;
                                    Marshal.WriteInt64(result, 16, 2);
                                    Marshal.WriteInt32(result, 24, cost & 0x3fffffff);
                                    Marshal.WriteInt32(result, 28, cost >> 30);
                                }
                                else
                                {
                                    Marshal.WriteInt64(result, 16, cost == 0 ? 0 : 1);
                                    Marshal.WriteInt32(result, 24, cost);
                                }
                                if (reportedLevel != level)
                                {
                                    reportedLevel = level;
                                    Entrypoint.Log($"Weapon upgrade price: current +{level} -> +{(long)level + 1}, cost={cost} copper.");
                                }
                                return result;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Entrypoint.Log($"Weapon upgrade hook disabled after error; using original prices: {ex}");
            }
        }
        return original(frame, throwFlag);
    }

    private static void Discover()
    {
        var thread = Marshal.ReadIntPtr(module + ThreadStateRva);
        if (thread == IntPtr.Zero) return;
        var interpreter = Marshal.ReadIntPtr(thread, 16);
        var modules = Marshal.ReadIntPtr(interpreter, 16);
        var warData = Find(modules, "cl_wardata");
        if (warData == 0) return;
        var klass = Find(Marshal.ReadIntPtr(warData, 16), "CWarData");
        if (klass == 0) return;
        var function = Find(Marshal.ReadIntPtr(klass, 264), "GetWeaponUpgradeCost");
        if (function == 0) return;
        var code = Marshal.ReadIntPtr(function, 16);
        if (Marshal.ReadInt32(code, 16) != 2 || Text(Marshal.ReadIntPtr(code, 104)) != "GetWeaponUpgradeCost")
            throw new InvalidOperationException("Unexpected Python cost function signature.");
        var constants = Marshal.ReadIntPtr(code, 48);
        var names = Marshal.ReadIntPtr(code, 56);
        if (!TupleContains(constants, "WeaponUpGradeCost") || !TupleContains(names, "GetWeaponNewGrade"))
            throw new InvalidOperationException("Unexpected Python cost function body.");
        // Obtain the existing int type from Python's builtins instead of guessing its address.
        var builtins = Find(modules, "builtins");
        intType = Find(Marshal.ReadIntPtr(builtins, 16), "int");
        allocateInt = Marshal.GetDelegateForFunctionPointer<Allocate>(Marshal.ReadIntPtr(intType, 304));
        Marshal.WriteInt64(code, Marshal.ReadInt64(code) + 1); // Keep code alive until process exit.
        targetCode = code;
        Marshal.WriteIntPtr(filter, 6, code);
        Entrypoint.Log("Shared Craftsman price function resolved. Custom pricing applies to quotes and charges generated by the local gameplay logic.");
        Verify(function, code);
    }

    private static bool TupleContains(nint tuple, string text)
    {
        for (int i = 0; i < Marshal.ReadInt64(tuple, 16); i++)
        {
            var item = Marshal.ReadIntPtr(tuple, 24 + i * 8);
            if (TypeName(item) == "str" && Text(item) == text) return true;
        }
        return false;
    }

    // Read CPython 3.6's compact dictionaries, only while the GIL is held.
    private static nint Find(nint dictionary, string name)
    {
        if (dictionary == 0) return 0;
        var keys = Marshal.ReadIntPtr(dictionary, 32);
        var values = Marshal.ReadIntPtr(dictionary, 40);
        long size = Marshal.ReadInt64(keys, 8), count = Marshal.ReadInt64(keys, 32);
        if (size <= 0 || size > 1_000_000 || count < 0 || count > 1_000_000)
            throw new InvalidOperationException("Invalid Python dictionary layout.");
        int width = size <= 128 ? 1 : size <= 32768 ? 2 : 4;
        var entries = keys + 40 + (int)(size * width);
        for (int i = 0; i < count; i++)
        {
            var key = Marshal.ReadIntPtr(entries, i * 24 + 8);
            var value = values == IntPtr.Zero ? Marshal.ReadIntPtr(entries, i * 24 + 16) : Marshal.ReadIntPtr(values, i * 8);
            if (key != IntPtr.Zero && value != IntPtr.Zero && TypeName(key) == "str" && Text(key) == name) return value;
        }
        return 0;
    }

    private static string TypeName(nint obj) => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj, 8), 24)) ?? "";

    private static string Text(nint obj)
    {
        long length = Marshal.ReadInt64(obj, 16);
        int state = Marshal.ReadInt32(obj, 32);
        if (length < 0 || length > 1024 || (state & 0x60) != 0x60) return "";
        return Marshal.PtrToStringUTF8(obj + 48, (int)length);
    }

    private static void Release(nint obj)
    {
        long references = Marshal.ReadInt64(obj) - 1;
        Marshal.WriteInt64(obj, references);
        if (references == 0)
            Marshal.GetDelegateForFunctionPointer<Destroy>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj, 8), 48))(obj);
    }

    private static void Verify(nint function, nint code)
    {
        if (!Settings.Upgrades.Enabled) return;
        // Exercise the real Python call path with an inert self value. The
        // replacement cost function only reads the current-level argument.
        var tupleType = Marshal.ReadIntPtr(Marshal.ReadIntPtr(code, 64), 8);
        var allocateTuple = Marshal.GetDelegateForFunctionPointer<Allocate>(Marshal.ReadIntPtr(tupleType, 304));
        var call = Marshal.GetDelegateForFunctionPointer<Call>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(function, 8), 128));
        var none = Marshal.ReadIntPtr(Marshal.ReadIntPtr(code, 48), 24);
        foreach (int level in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 35, 100, 1_000_000_000 })
        {
            var args = allocateTuple(tupleType, 2);
            var number = allocateInt(intType, 1);
            if (args == 0 || number == 0) throw new InvalidOperationException("Cannot allocate pricing verification arguments.");
            Marshal.WriteInt64(number, 16, level == 0 ? 0 : 1);
            Marshal.WriteInt32(number, 24, level);
            Marshal.WriteInt64(none, Marshal.ReadInt64(none) + 1);
            Marshal.WriteIntPtr(args, 24, none);
            Marshal.WriteIntPtr(args, 32, number);
            var result = call(function, args, 0);
            if (result == 0) throw new InvalidOperationException("Python pricing verification returned an exception.");
            long digits = Marshal.ReadInt64(result, 16);
            long actual = digits == 0 ? 0 : Marshal.ReadInt32(result, 24);
            if (digits == 2) actual += (long)Marshal.ReadInt32(result, 28) << 30;
            Release(result);
            Release(args);
            if (actual != Settings.Upgrades.Cost(level)) throw new InvalidOperationException($"Price check failed at +{level}: {actual}.");
        }
        reportedLevel = -1;
        Entrypoint.Log("Pricing verification passed through the real Python function for levels 0-10, 35, 100, and overflow saturation. No weapon or copper state was changed.");
    }
}
