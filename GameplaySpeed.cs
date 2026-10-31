using System.Globalization;
using System.Runtime.InteropServices;
using Doorstop;

namespace MiniSpeed;

internal static class GameplaySpeed
{
    internal static nint BaseCode, RefreshCode;
    private static nint cellRec;
    private static int refreshMask;
    private static bool failed;
    private static Func<int, nint> createInt = null!;
    private static Action<nint> release = null!;
    private static readonly Dictionary<nint, (nint Owner, double Original, double Applied)> Bases = new();

    internal static bool Initialize(nint modules, Func<int, nint> allocate, Action<nint> destroy)
    {
        if (BaseCode != 0) return true;
        var module = UpgradeHook.Find(modules, "cl_object.baseattr");
        if (module == 0) return false;
        var dict = Marshal.ReadIntPtr(module, 16);
        var speedClass = UpgradeHook.Find(dict, "CMoveSpeed");
        var baseClass = UpgradeHook.Find(dict, "CBaseAttr");
        if (speedClass == 0 || baseClass == 0) return false;
        var change = UpgradeHook.Find(Marshal.ReadIntPtr(speedClass, 264), "ChangeBase");
        var refresh = UpgradeHook.Find(Marshal.ReadIntPtr(baseClass, 264), "Refresh");
        cellRec = UpgradeHook.Find(dict, "CELL_REC");
        if (change == 0 || refresh == 0 || cellRec == 0) return false;
        var changeCode = Marshal.ReadIntPtr(change, 16);
        var refreshCode = Marshal.ReadIntPtr(refresh, 16);
        if (Marshal.ReadInt32(changeCode, 16) != 3 || Marshal.ReadInt32(refreshCode, 16) != 2 ||
            UpgradeHook.Text(Marshal.ReadIntPtr(changeCode, 104)) != "ChangeBase" ||
            !HasName(changeCode, "m_BaseValue") || !HasName(refreshCode, "CalCurValue"))
            throw new InvalidOperationException("Unexpected movement attribute function signature/body.");
        createInt = allocate;
        release = destroy;
        refreshMask = checked((int)Number(UpgradeHook.Find(dict, "MASK_REFRESH_VALUE")));
        Marshal.WriteInt64(changeCode, Marshal.ReadInt64(changeCode) + 1);
        Marshal.WriteInt64(refreshCode, Marshal.ReadInt64(refreshCode) + 1);
        BaseCode = changeCode;
        RefreshCode = refreshCode;
        Entrypoint.Log("Character base-speed hook installed: CMoveSpeed.ChangeBase, with base restoration on attribute refresh. Only CCtrlHero is modified; game speed bonuses are calculated afterward.");
        return true;
    }

    private static bool HasName(nint code, string name)
    {
        var names = Marshal.ReadIntPtr(code, 56);
        for (int i = 0; i < Marshal.ReadInt64(names, 16); i++)
            if (UpgradeHook.Text(Marshal.ReadIntPtr(names, 24 + i * 8)) == name) return true;
        return false;
    }

    // Access heap-instance dictionaries using the validated CPython type layout.
    private static nint InstanceDict(nint obj)
    {
        var type = Marshal.ReadIntPtr(obj, 8);
        long offset = Marshal.ReadInt64(type, 288);
        if (offset < 0) offset += Marshal.ReadInt64(type, 32);
        if (offset <= 0 || offset >= 4096) throw new InvalidOperationException("Unsupported gameplay instance dictionary layout.");
        return Marshal.ReadIntPtr(obj, (int)offset);
    }

    private static double Number(nint obj)
    {
        if (UpgradeHook.TypeName(obj) == "float") return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(obj, 16));
        if (UpgradeHook.TypeName(obj) == "int")
        {
            long size = Marshal.ReadInt64(obj, 16);
            if (size == 0) return 0;
            if (Math.Abs(size) == 1) return (double)Marshal.ReadInt32(obj, 24) * Math.Sign(size);
        }
        throw new InvalidOperationException("Unsupported base-speed number.");
    }

    internal static void OnFrame(nint frame)
    {
        if (failed) return;
        try { ApplyFrame(frame); }
        catch (Exception ex)
        {
            failed = true;
            Entrypoint.Log($"Base-speed hook stopped after error: {ex}");
        }
    }

    private static void ApplyFrame(nint frame)
    {
        var code = Marshal.ReadIntPtr(frame, 32);
        if (code != BaseCode && code != RefreshCode) return;
        var locals = Marshal.ReadIntPtr(frame, 64) - Marshal.ReadInt32(code, 24) * 8;
        var attr = Marshal.ReadIntPtr(locals);
        var owner = Marshal.ReadIntPtr(locals, 8);
        if (UpgradeHook.TypeName(owner) != "CCtrlHero" || UpgradeHook.TypeName(attr) != "CMoveSpeed") return;
        lock (SpeedHook.SpeedLock)
        {
            if (code == BaseCode)
            {
                // Replace only this frame's input; never mutate a shared Python integer.
                var input = Marshal.ReadIntPtr(locals, 16);
                double original = Number(input);
                if (original <= 0) return;
                // Callable base formulas are intentionally left to the game's implementation.
                var dict = InstanceDict(attr);
                var formula = UpgradeHook.Find(dict, "m_BaseFormula");
                if (formula != 0 && UpgradeHook.TypeName(formula) != "NoneType") return;
                int modified = Scale(original);
                var replacement = createInt(modified);
                Marshal.WriteIntPtr(locals, 16, replacement); // Frame now owns the new reference.
                release(input);
                Bases[attr] = (owner, original, modified);
                Log(owner, original, modified, "base input");
            }
            else if (Bases.TryGetValue(attr, out var saved) && saved.Owner == owner)
            {
                int modified = Scale(saved.Original);
                if (modified == saved.Applied) return;
                // Stored CMoveSpeed.m_BaseValue is input * CELL_REC. Replace the
                // boxed value, then let the existing Refresh calculate all bonuses.
                var dict = InstanceDict(attr);
                var stored = UpgradeHook.Find(dict, "m_BaseValue");
                double conversion = Number(cellRec);
                if (stored == 0 || Math.Abs(Number(stored) - saved.Applied * conversion) > 0.00001)
                    throw new InvalidOperationException("Base speed changed outside ChangeBase; refusing to overwrite an unknown base.");
                var floatType = Marshal.ReadIntPtr(stored, 8);
                if (UpgradeHook.TypeName(stored) != "float") throw new InvalidOperationException("Expected a floating-point internal movement base.");
                var allocate = Marshal.GetDelegateForFunctionPointer<FloatAllocate>(Marshal.ReadIntPtr(floatType, 304));
                var replacement = allocate(floatType, 0);
                if (replacement == 0) throw new InvalidOperationException("Cannot allocate refreshed base speed.");
                Marshal.WriteInt64(replacement, 16, BitConverter.DoubleToInt64Bits(modified * conversion));
                ReplaceDictionaryValue(dict, stored, replacement);
                var refresh = UpgradeHook.Find(dict, "m_Refresh");
                if (refresh != 0)
                {
                    var mask = createInt(checked((int)Number(refresh)) | refreshMask);
                    ReplaceDictionaryValue(dict, refresh, mask, "m_Refresh");
                }
                Bases[attr] = (owner, saved.Original, modified);
                Log(owner, saved.Original, modified, "attribute refresh");
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint FloatAllocate(nint type, nint count);

    private static void ReplaceDictionaryValue(nint dict, nint oldValue, nint replacement, string name = "m_BaseValue")
    {
        var keys = Marshal.ReadIntPtr(dict, 32);
        var values = Marshal.ReadIntPtr(dict, 40);
        long size = Marshal.ReadInt64(keys, 8), count = Marshal.ReadInt64(keys, 32);
        int width = size <= 128 ? 1 : size <= 32768 ? 2 : 4;
        var entries = keys + 40 + (int)(size * width);
        for (int i = 0; i < count; i++)
        {
            var key = Marshal.ReadIntPtr(entries, i * 24 + 8);
            if (key == IntPtr.Zero || UpgradeHook.TypeName(key) != "str" || UpgradeHook.Text(key) != name) continue;
            var slot = values == IntPtr.Zero ? entries + i * 24 + 16 : values + i * 8;
            if (Marshal.ReadIntPtr(slot) != oldValue) break;
            Marshal.WriteIntPtr(slot, replacement);
            release(oldValue);
            return;
        }
        release(replacement);
        throw new InvalidOperationException("Cannot replace movement base dictionary value.");
    }

    private static int Scale(double original) => (int)Math.Clamp(Math.Round(original * (Settings.Enabled ? Settings.Multiplier : 1.0)), 0, int.MaxValue);

    private static void Log(nint owner, double original, int modified, string reason)
    {
        var dict = InstanceDict(owner);
        var sid = UpgradeHook.Find(dict, "m_SID");
        Entrypoint.Log($"Hero base speed applied ({reason}): SID={(sid == 0 ? "unknown" : Number(sid).ToString(CultureInfo.InvariantCulture))}, original={original.ToString(CultureInfo.InvariantCulture)}, modified={modified}, Enabled={Settings.Enabled}, SpeedMultiplier={Settings.Multiplier.ToString(CultureInfo.InvariantCulture)}. Game bonuses/penalties are applied after this base.");
    }
}
