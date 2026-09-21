using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GalaxyCommand.Simulation;

internal static class DirectoryDurability
{
    private const int ReadOnly = 0;

    /// <summary>
    /// Synchronizes directory-entry changes on Unix platforms that expose
    /// directory file descriptors; Windows has no equivalent supported here.
    /// </summary>
    internal static void Synchronize(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        int descriptor = Open(path, ReadOnly);
        if (descriptor < 0)
        {
            throw NativeFailure("open");
        }

        try
        {
            if (Fsync(descriptor) != 0)
            {
                throw NativeFailure("synchronize");
            }
        }
        finally
        {
            _ = Close(descriptor);
        }
    }

    private static IOException NativeFailure(string operation) =>
        new(
            $"Unable to {operation} the save directory.",
            new Win32Exception(Marshal.GetLastPInvokeError()));

    [DllImport(
        "libc",
        EntryPoint = "open",
        ExactSpelling = true,
        CharSet = CharSet.Ansi,
        BestFitMapping = false,
        ThrowOnUnmappableChar = true,
        SetLastError = true)]
    private static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", ExactSpelling = true, SetLastError = true)]
    private static extern int Fsync(int descriptor);

    [DllImport("libc", EntryPoint = "close", ExactSpelling = true, SetLastError = true)]
    private static extern int Close(int descriptor);
}
