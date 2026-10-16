using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Hi3Helper.Plugin.Core;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1416
#pragma warning disable SYSLIB0057

namespace Hi3Helper.Hypergryph.Core.Management;

/// <summary>
///     Launches a game as a child of a suspended helper process.
///     <para>
///         Recent Endfield builds verify that their direct parent process is a
///         Hypergryph-signed executable named <c>games.exe</c>. This bridge satisfies
///         that requirement without depending on the official launcher: it picks a
///         Hypergryph-signed executable that already ships inside the game directory
///         (for example <c>PlatformProcess.exe</c>), copies it as <c>games.exe</c> into
///         a private directory under the user temporary folder, creates the copy
///         suspended, and uses it only as the direct parent process of the game.
///     </para>
///     <para>
///         The helper never executes any code: it stays suspended from the moment it is
///         created until it is terminated after the game exits or after the bridge is
///         disposed.
///     </para>
/// </summary>
public sealed partial class ParentProcessBridge : IDisposable
{
    /// <summary>
    ///     The file name the game client expects for its parent process.
    /// </summary>
    private const string HelperFileName = "games.exe";

    /// <summary>
    ///     Hypergryph-signed executables that ship with the game client, in order of
    ///     preference. Every candidate is validated with
    ///     <see cref="IsTrustedPublisher" /> before it is used.
    /// </summary>
    private static readonly string[] HelperSourceCandidates =
    [
        "PlatformProcess.exe",
        "UnityCrashHandler64.exe",
        "Endfield.exe"
    ];

    private const uint CreateSuspended              = 0x00000004;
    private const uint CreateNoWindow               = 0x08000000;
    private const uint ExtendedStartupInfoPresent   = 0x00080000;
    private const nint ProcThreadAttributeParentProcess = 0x00020000;

    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    private static readonly string[] TrustedPublishers = ["Hypergryph", "GRYPHLINE"];

    private nint _helperProcess;
    private nint _helperThread;
    private bool _disposed;

    private ParentProcessBridge(nint process, nint thread)
    {
        _helperProcess = process;
        _helperThread  = thread;
    }

    /// <summary>
    ///     Prepares the suspended helper process for the given game directory.
    /// </summary>
    /// <param name="gameDirectory">The directory that contains the game executable.</param>
    /// <param name="bridge">The prepared bridge when the method returns <c>true</c>.</param>
    /// <param name="failureReason">A short diagnostic message when the method returns <c>false</c>.</param>
    /// <returns><c>true</c> when the bridge is ready to launch the game.</returns>
    public static bool TryCreate(string? gameDirectory, [NotNullWhen(true)] out ParentProcessBridge? bridge,
        out string? failureReason)
    {
        bridge        = null;
        failureReason = null;

        if (string.IsNullOrEmpty(gameDirectory) || !Directory.Exists(gameDirectory))
        {
            failureReason = $"the game directory is not available: {gameDirectory}";
            return false;
        }

        string? helperSourcePath = FindHelperSource(gameDirectory);
        if (helperSourcePath == null)
        {
            failureReason = "no signed Hypergryph executable was found in the game directory";
            return false;
        }

        try
        {
            string helperDirectory = Path.Combine(Path.GetTempPath(), "Hi3Helper", "ParentBridge");
            Directory.CreateDirectory(helperDirectory);

            string helperPath = Path.Combine(helperDirectory, HelperFileName);

            TerminateStaleHelpers(helperPath);
            CopyHelper(helperSourcePath, helperPath);

            if (!TryCreateSuspended(helperPath, out nint process, out nint thread))
            {
                failureReason = "the suspended helper process could not be created";
                return false;
            }

            bridge = new ParentProcessBridge(process, thread);
            SharedStatic.InstanceLogger.LogInformation(
                $"[ParentBridge] Helper process prepared: {helperSourcePath} -> {helperPath}");
            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            SharedStatic.InstanceLogger.LogWarning(
                $"[ParentBridge] Cannot prepare the helper process: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Starts <paramref name="targetExecutablePath" /> as a child of the suspended
    ///     helper process.
    /// </summary>
    public Process StartGame(string targetExecutablePath, string? arguments, string? workingDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        nint attributeListSize = 0;
        InitializeProcThreadAttributeList(nint.Zero, 1, 0, ref attributeListSize);

        nint attributeList = Marshal.AllocHGlobal(attributeListSize);
        nint commandLine   = nint.Zero;
        bool attributeListInitialized = false;

        try
        {
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "InitializeProcThreadAttributeList failed.");
            }

            attributeListInitialized = true;

            unsafe
            {
                nint parent = _helperProcess;
                if (!UpdateProcThreadAttribute(attributeList, 0, ProcThreadAttributeParentProcess,
                        (nint)(&parent), (nint)sizeof(nint), nint.Zero, nint.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "UpdateProcThreadAttribute failed.");
                }
            }

            string commandLineText = string.IsNullOrEmpty(arguments)
                ? $"\"{targetExecutablePath}\""
                : $"\"{targetExecutablePath}\" {arguments}";
            commandLine = Marshal.StringToCoTaskMemUni(commandLineText);

            STARTUPINFOEX startupInfo = default;
            startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startupInfo.lpAttributeList = attributeList;

            if (!CreateProcess(targetExecutablePath, commandLine, nint.Zero, nint.Zero, false,
                    ExtendedStartupInfoPresent, nint.Zero, workingDirectory, in startupInfo,
                    out PROCESS_INFORMATION processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW failed.");
            }

            int processId = processInformation.dwProcessId;
            CloseHandle(processInformation.hThread);
            CloseHandle(processInformation.hProcess);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return Process.GetProcessById(processId);
                }
                catch (ArgumentException) when (attempt < 9)
                {
                    Thread.Sleep(100);
                }
            }
        }
        finally
        {
            if (commandLine != nint.Zero)
            {
                Marshal.FreeCoTaskMem(commandLine);
            }

            if (attributeListInitialized)
            {
                DeleteProcThreadAttributeList(attributeList);
            }

            Marshal.FreeHGlobal(attributeList);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_helperProcess != nint.Zero)
        {
            TerminateProcess(_helperProcess, 0);
            CloseHandle(_helperProcess);
            _helperProcess = nint.Zero;
        }

        if (_helperThread != nint.Zero)
        {
            CloseHandle(_helperThread);
            _helperThread = nint.Zero;
        }
    }

    private static string? FindHelperSource(string gameDirectory)
    {
        foreach (string candidateName in HelperSourceCandidates)
        {
            string candidatePath = Path.Combine(gameDirectory, candidateName);
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            if (!IsTrustedPublisher(candidatePath))
            {
                continue;
            }

            return candidatePath;
        }

        return null;
    }

    private static bool IsTrustedPublisher(string executablePath)
    {
        uint result = WinVerifyTrustFile(executablePath);
        if (result != 0)
        {
            SharedStatic.InstanceLogger.LogWarning(
                $"[ParentBridge] Signature verification failed (0x{result:x8}): {executablePath}");
            return false;
        }

        try
        {
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(executablePath);
            string subject = certificate.Subject;

            if (TrustedPublishers.Any(publisher =>
                    subject.Contains(publisher, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            SharedStatic.InstanceLogger.LogWarning($"[ParentBridge] Unexpected signer: {subject}");
            return false;
        }
        catch (Exception ex)
        {
            SharedStatic.InstanceLogger.LogWarning(
                $"[ParentBridge] Cannot read the signer certificate: {ex.Message}");
            return false;
        }
    }

    private static void TerminateStaleHelpers(string helperPath)
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(helperPath)))
        {
            try
            {
                if (!string.Equals(process.MainModule?.FileName, helperPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SharedStatic.InstanceLogger.LogInformation(
                    $"[ParentBridge] Terminating a stale helper process (PID {process.Id}).");
                process.Kill();
                process.WaitForExit(2000);
            }
            catch (Exception ex)
            {
                SharedStatic.InstanceLogger.LogWarning(
                    $"[ParentBridge] Cannot terminate a stale helper process: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void CopyHelper(string sourcePath, string helperPath)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(sourcePath, helperPath, true);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                // A previous helper process may still be shutting down, which keeps the
                // file mapped. Give it a moment and retry.
                Thread.Sleep(150);
            }
        }
    }

    private static bool TryCreateSuspended(string executablePath, out nint process, out nint thread)
    {
        process = nint.Zero;
        thread  = nint.Zero;

        STARTUPINFOEX startupInfo = default;
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        if (!CreateProcess(executablePath, nint.Zero, nint.Zero, nint.Zero, false,
                CreateSuspended | CreateNoWindow, nint.Zero, Path.GetDirectoryName(executablePath),
                in startupInfo, out PROCESS_INFORMATION processInformation))
        {
            int error = Marshal.GetLastWin32Error();
            SharedStatic.InstanceLogger.LogWarning(
                $"[ParentBridge] Cannot create the suspended helper process: {new Win32Exception(error).Message}");
            return false;
        }

        process = processInformation.hProcess;
        thread  = processInformation.hThread;
        return true;
    }

    private static uint WinVerifyTrustFile(string filePath)
    {
        nint nativeFilePath  = Marshal.StringToCoTaskMemUni(filePath);
        nint nativeFileInfo  = nint.Zero;
        nint nativeTrustData = nint.Zero;

        try
        {
            WINTRUST_FILE_INFO fileInfo = default;
            fileInfo.cbStruct      = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>();
            fileInfo.pcwszFilePath = nativeFilePath;

            nativeFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, nativeFileInfo, false);

            WINTRUST_DATA trustData = default;
            trustData.cbStruct            = (uint)Marshal.SizeOf<WINTRUST_DATA>();
            trustData.dwUIChoice          = 2;
            trustData.fdwRevocationChecks = 0;
            trustData.dwUnionChoice       = 1;
            trustData.pFile               = nativeFileInfo;
            trustData.dwStateAction       = 1;
            trustData.dwProvFlags         = 0x100;

            nativeTrustData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            Marshal.StructureToPtr(trustData, nativeTrustData, false);

            uint result = WinVerifyTrust(nint.Zero, in WinTrustActionGenericVerifyV2, nativeTrustData);

            trustData.dwStateAction = 2;
            Marshal.StructureToPtr(trustData, nativeTrustData, false);
            WinVerifyTrust(nint.Zero, in WinTrustActionGenericVerifyV2, nativeTrustData);

            return result;
        }
        finally
        {
            if (nativeTrustData != nint.Zero)
            {
                Marshal.FreeHGlobal(nativeTrustData);
            }

            if (nativeFileInfo != nint.Zero)
            {
                Marshal.FreeHGlobal(nativeFileInfo);
            }

            Marshal.FreeCoTaskMem(nativeFilePath);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public nint pcwszFilePath;
        public nint hFile;
        public nint pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public nint pFile;
        public uint dwStateAction;
        public nint hWVTStateData;
        public nint pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public nint pSignatureSettings;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(string? lpApplicationName, nint lpCommandLine,
        nint lpProcessAttributes, nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment,
        string? lpCurrentDirectory, in STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint lpAttributeList, int dwAttributeCount,
        int dwFlags, ref nint lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(nint lpAttributeList, uint dwFlags, nint attribute,
        nint lpValue, nint cbSize, nint lpPreviousValue, nint lpReturnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint lpAttributeList);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint hProcess, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [LibraryImport("wintrust.dll", EntryPoint = "WinVerifyTrust")]
    private static partial uint WinVerifyTrust(nint hwnd, in Guid pgActionID, nint pWVTData);
}
