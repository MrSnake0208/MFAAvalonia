using System;

namespace MFAAvalonia.Extensions.MaaFW;

public enum MaaControllerTypes
{
    None = 0,
    Win32 = 1,
    Adb = 2,
}

public static class MaaControllerHelper
{
    public static string ToResourceKey(this MaaControllerTypes type)
    {
        return type switch
        {
            MaaControllerTypes.Win32 => "TabWin32",
            MaaControllerTypes.Adb => "TabADB",
            _ => "TabADB"
        };
    }

    public static MaaControllerTypes ToMaaControllerTypes(this string? type, MaaControllerTypes defaultValue = MaaControllerTypes.Adb)
    {
        if (string.IsNullOrWhiteSpace(type))
            return defaultValue;
        if (type.Contains("win32", StringComparison.OrdinalIgnoreCase))
            return MaaControllerTypes.Win32;
        if (type.Contains("adb", StringComparison.OrdinalIgnoreCase))
            return MaaControllerTypes.Adb;
        return defaultValue;
    }
}
