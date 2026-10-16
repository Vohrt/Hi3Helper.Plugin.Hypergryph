using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

#pragma warning disable CA1416

namespace Hi3Helper.Hypergryph.Core.Management;

internal enum LauncherAcquireChoice
{
    Download,
    PickExecutable,
    Cancel
}

internal static partial class NativePrompt
{
    private const uint MbYesNoCancel   = 0x00000003;
    private const uint MbYesNo         = 0x00000004;
    private const uint MbOk            = 0x00000000;
    private const uint MbIconQuestion  = 0x00000020;
    private const uint MbIconWarning   = 0x00000030;
    private const uint MbIconInfo      = 0x00000040;
    private const uint MbTopMost       = 0x00040000;
    private const uint MbSetForeground = 0x00010000;

    private const int IdYes = 6;
    private const int IdNo  = 7;

    private const uint OfnHideReadOnly    = 0x00000004;
    private const uint OfnNoChangeDir     = 0x00000008;
    private const uint OfnPathMustExist   = 0x00000800;
    private const uint OfnFileMustExist   = 0x00001000;
    private const uint OfnExplorer        = 0x00080000;
    private const uint OfnDontAddToRecent = 0x02000000;

    private const int PathBufferLength = 1024;

    private const string LauncherPattern = "Games.exe";
    private const string AllFilesPattern = "*.*";

    private const uint PromptFlags = MbIconQuestion | MbTopMost | MbSetForeground;

    public static LauncherAcquireChoice AskAcquire(string zoneFullName, long packageSize)
    {
        return RunOnSta(() =>
        {
            PromptStrings text = PromptStrings.Current;

            if (packageSize > 0)
            {
                int download = MessageBox(nint.Zero, BuildDownloadText(text, zoneFullName, packageSize),
                    text.Caption, MbYesNoCancel | PromptFlags);

                if (download == IdYes) return LauncherAcquireChoice.Download;
                if (download != IdNo) return LauncherAcquireChoice.Cancel;
            }

            int pick = MessageBox(nint.Zero, BuildPickText(text, zoneFullName, packageSize > 0), text.Caption,
                MbYesNo | PromptFlags);

            return pick == IdYes ? LauncherAcquireChoice.PickExecutable : LauncherAcquireChoice.Cancel;
        });
    }

    public static string? AskLauncherExecutable()
    {
        return RunOnSta(() =>
        {
            PromptStrings text = PromptStrings.Current;

            nint title  = Marshal.StringToHGlobalUni(text.PickerTitle);
            nint filter = Marshal.StringToHGlobalUni(BuildLauncherFilter(text));
            nint buffer = Marshal.AllocHGlobal(PathBufferLength * sizeof(char));

            try
            {
                Marshal.WriteInt16(buffer, 0);

                OPENFILENAMEW dialog = default;
                dialog.lStructSize  = (uint)Marshal.SizeOf<OPENFILENAMEW>();
                dialog.lpstrFilter  = filter;
                dialog.nFilterIndex = 1;
                dialog.lpstrFile    = buffer;
                dialog.nMaxFile     = PathBufferLength;
                dialog.lpstrTitle   = title;
                dialog.Flags        = OfnHideReadOnly | OfnNoChangeDir | OfnPathMustExist
                                      | OfnFileMustExist | OfnExplorer | OfnDontAddToRecent;

                if (!GetOpenFileName(ref dialog)) return null;

                string? path = Marshal.PtrToStringUni(buffer);
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                Marshal.FreeHGlobal(filter);
                Marshal.FreeHGlobal(title);
            }
        });
    }

    public static void WarnInvalidLauncher(string executablePath)
    {
        RunOnSta(() =>
        {
            PromptStrings text = PromptStrings.Current;

            MessageBox(nint.Zero, string.Format(text.InvalidLauncher, executablePath), text.Caption,
                MbOk | MbIconWarning | PromptFlags);
            return true;
        });
    }

    public static void NotifyComponentReady()
    {
        RunOnSta(() =>
        {
            PromptStrings text = PromptStrings.Current;

            MessageBox(nint.Zero, text.ComponentReady, text.Caption, MbOk | MbIconInfo | PromptFlags);
            return true;
        });
    }

    public static void NotifyDownloadFailed()
    {
        RunOnSta(() =>
        {
            PromptStrings text = PromptStrings.Current;

            MessageBox(nint.Zero, text.DownloadFailed, text.Caption, MbOk | MbIconWarning | PromptFlags);
            return true;
        });
    }

    private static string BuildLauncherFilter(PromptStrings text)
        => $"{text.PickerFilterLauncher}\0{LauncherPattern}\0{text.PickerFilterAllFiles}\0{AllFilesPattern}\0";

    private static string BuildDownloadText(PromptStrings text, string zoneFullName, long packageSize)
        => string.Format(text.DownloadAsk, zoneFullName, packageSize / 1024d / 1024d);

    private static string BuildPickText(PromptStrings text, string zoneFullName, bool afterDecliningDownload)
        => afterDecliningDownload ? text.PickAsk : string.Format(text.PickAskNoPackage, zoneFullName);

    private static T RunOnSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? failure = null;

        Thread thread = new(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OPENFILENAMEW
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public nint lpstrFilter;
        public nint lpstrCustomFilter;
        public uint nMaxCustFilter;
        public uint nFilterIndex;
        public nint lpstrFile;
        public uint nMaxFile;
        public nint lpstrFileTitle;
        public uint nMaxFileTitle;
        public nint lpstrInitialDir;
        public nint lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public nint lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public uint dwReserved;
        public uint FlagsEx;
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint hWnd, string lpText, string lpCaption, uint uType);

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileName(ref OPENFILENAMEW lpofn);
}
