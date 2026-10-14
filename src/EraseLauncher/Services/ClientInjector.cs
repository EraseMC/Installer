using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EraseLauncher.Services;

/// <summary>Loads a DLL into another process by running LoadLibraryW on a remote thread.</summary>
internal static class ClientInjector
{
    private const uint ProcessAccess = 0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x0400; // CREATE_THREAD | VM_OPERATION | VM_READ | VM_WRITE | QUERY_INFORMATION
    private const uint MemCommitReserve = 0x1000 | 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint WaitObject0 = 0;
    private const uint LoadTimeoutMs = 15000;

    public static void LoadLibrary(int processId, string dllPath)
    {
        using var process = OpenProcess(ProcessAccess, false, processId);
        if (process.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the Minecraft process.");
        }

        // kernel32 is mapped at the same address in every process of a session.
        var loadLibrary = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
        if (loadLibrary == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not find LoadLibraryW.");
        }

        var path = Encoding.Unicode.GetBytes(dllPath + '\0');
        var remote = VirtualAllocEx(process, IntPtr.Zero, (nuint)path.Length, MemCommitReserve, PageReadWrite);
        if (remote == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not allocate memory in Minecraft.");
        }

        try
        {
            if (!WriteProcessMemory(process, remote, path, (nuint)path.Length, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not write the client path into Minecraft.");
            }

            using var thread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remote, 0, out _);
            if (thread.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the loader thread in Minecraft.");
            }

            if (WaitForSingleObject(thread, LoadTimeoutMs) != WaitObject0)
            {
                // The thread still reads the path, so the memory is deliberately not freed.
                remote = IntPtr.Zero;
                throw new TimeoutException("Minecraft did not finish loading the client in time.");
            }
        }
        finally
        {
            if (remote != IntPtr.Zero)
            {
                VirtualFreeEx(process, remote, 0, MemRelease);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(SafeProcessHandle process, IntPtr address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(SafeProcessHandle process, IntPtr address, nuint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(SafeProcessHandle process, IntPtr address, byte[] buffer, nuint size, out nuint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle process, IntPtr attributes, nuint stackSize, IntPtr startAddress, IntPtr parameter, uint creationFlags, out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
}
