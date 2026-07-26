using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AnimeGoNet.App.Downloads;

internal static partial class HardLinkCapability
{
    public static void Create(string targetPath, string existingPath)
    {
        var succeeded = OperatingSystem.IsWindows()
            ? CreateHardLinkWindows(targetPath, existingPath, IntPtr.Zero)
            : OperatingSystem.IsMacOS()
                ? LinkMacOs(existingPath, targetPath) == 0
                : LinkUnix(existingPath, targetPath) == 0;
        if (!succeeded)
        {
            throw new IOException(
                "Hard link creation failed.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateHardLinkW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkWindows(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);

    [LibraryImport(
        "libc",
        EntryPoint = "link",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LinkUnix(string existingPath, string newPath);

    [LibraryImport(
        "libSystem.B.dylib",
        EntryPoint = "link",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LinkMacOs(string existingPath, string newPath);
}
