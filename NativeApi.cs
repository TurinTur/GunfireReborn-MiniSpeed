using System.Runtime.InteropServices;

namespace MiniSpeed;

internal static class NativeApi
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string name);

    [DllImport("kernel32", CharSet = CharSet.Ansi)]
    private static extern nint GetProcAddress(nint module, string name);

    internal static T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(GetProcAddress(GetModuleHandle("GameAssembly.dll"), name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint OnePtr(nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint Assemblies(nint domain, out nuint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint ClassFromName(nint image, [MarshalAs(UnmanagedType.LPUTF8Str)] string ns, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint Iterator(nint klass, ref nint iterator);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint NamedMember(nint klass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint Method(nint klass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void StaticValue(nint field, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FieldValue(nint obj, nint field, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint Invoke(nint method, nint obj, nint args, out nint exception);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint ParamCount(nint method);

    internal static string Text(nint value) => Marshal.PtrToStringUTF8(value) ?? "";
}
