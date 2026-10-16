using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Hi3Helper.Plugin.Core;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1416

namespace Hi3Helper.Hypergryph.Core.Management;

internal enum LauncherProgressStage
{
    Downloading,
    Verifying,
    Extracting
}

internal sealed partial class ProgressPrompt : IDisposable
{
    private const string WindowClassName       = "Hi3HelperLauncherProgress";
    private const string StaticClassName       = "STATIC";
    private const string ProgressBarClassName  = "msctls_progress32";

    private const uint WsPopup     = 0x80000000;
    private const uint WsVisible   = 0x10000000;
    private const uint WsCaption   = 0x00C00000;
    private const uint WsSysMenu   = 0x00080000;
    private const uint WsChild     = 0x40000000;
    private const uint WsExTopMost = 0x00000008;

    private const uint CsHRedraw = 0x0002;
    private const uint CsVRedraw = 0x0001;

    private const uint SsLeft    = 0x00000000;
    private const uint PbsSmooth = 0x00000001;

    private const uint WmClose          = 0x0010;
    private const uint WmDestroy        = 0x0002;
    private const uint WmSetFont        = 0x0030;
    private const uint WmCtlColorStatic = 0x0138;
    private const uint PbmSetPos        = 0x0402;

    private const int SmCxScreen     = 0;
    private const int SmCyScreen     = 1;
    private const int SwShow         = 5;
    private const int DefaultGuiFont = 17;
    private const int ColorFaceIndex = 15;
    private const int Transparent    = 1;
    private const int IdcArrow       = 32512;
    private const int IccProgress    = 0x00000020;

    private const int WindowWidth  = 460;
    private const int WindowHeight = 150;
    private const int Margin       = 18;
    private const int TextHeight   = 22;
    private const int BarHeight    = 20;

    private readonly string _caption;
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private nint _window;
    private nint _text;
    private nint _bar;
    private volatile bool _closed;

    private ProgressPrompt(string caption) => _caption = caption;

    internal static ProgressPrompt Start(string caption)
    {
        ProgressPrompt prompt = new(caption);

        Thread thread = new(prompt.RunMessageLoop) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        prompt._thread = thread;

        thread.Start();
        prompt._ready.Wait();

        return prompt;
    }

    internal void Report(LauncherProgressStage stage, int percent)
    {
        if (_closed || _window == nint.Zero)
        {
            return;
        }

        PromptStrings text = PromptStrings.Current;

        string message = stage switch
        {
            LauncherProgressStage.Verifying  => text.ProgressVerifying,
            LauncherProgressStage.Extracting => text.ProgressExtracting,
            _                                => string.Format(text.ProgressDownloading, percent)
        };

        SetWindowText(_text, message);
        SendMessage(_bar, PbmSetPos, percent, nint.Zero);
    }

    public void Dispose()
    {
        if (_window != nint.Zero)
        {
            PostMessage(_window, WmClose, nint.Zero, nint.Zero);
        }

        _thread?.Join();
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        try
        {
            CreateProgressWindow();
        }
        catch (Exception ex)
        {
            SharedStatic.InstanceLogger.LogWarning($"[HgLauncher] Cannot show the progress window: {ex.Message}");
        }
        finally
        {
            _ready.Set();
        }

        if (_window == nint.Zero)
        {
            _closed = true;
            return;
        }

        while (GetMessage(out MSG message, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(in message);
            DispatchMessage(in message);
        }

        _closed = true;
    }

    private void CreateProgressWindow()
    {
        INITCOMMONCONTROLSEX commonControls = default;
        commonControls.dwSize = (uint)Marshal.SizeOf<INITCOMMONCONTROLSEX>();
        commonControls.dwICC  = IccProgress;
        InitCommonControlsEx(ref commonControls);

        nint instance    = GetModuleHandle(null);
        nint className   = Marshal.StringToHGlobalUni(WindowClassName);
        nint windowProc;

        unsafe
        {
            windowProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProc;
        }

        try
        {
            WNDCLASSEXW windowClass = default;
            windowClass.cbSize        = (uint)Marshal.SizeOf<WNDCLASSEXW>();
            windowClass.style         = CsHRedraw | CsVRedraw;
            windowClass.lpfnWndProc   = windowProc;
            windowClass.hInstance     = instance;
            windowClass.hCursor       = LoadCursor(nint.Zero, (nint)IdcArrow);
            windowClass.hbrBackground = (nint)(ColorFaceIndex + 1);
            windowClass.lpszClassName = className;

            RegisterClassEx(in windowClass);
        }
        finally
        {
            Marshal.FreeHGlobal(className);
        }

        int x = (GetSystemMetrics(SmCxScreen) - WindowWidth) / 2;
        int y = (GetSystemMetrics(SmCyScreen) - WindowHeight) / 2;

        _window = CreateWindowEx(WsExTopMost, WindowClassName, _caption,
            WsPopup | WsCaption | WsSysMenu | WsVisible, x, y, WindowWidth, WindowHeight,
            nint.Zero, nint.Zero, instance, nint.Zero);

        if (_window == nint.Zero)
        {
            throw new InvalidOperationException(
                $"CreateWindowExW failed with error {Marshal.GetLastWin32Error()}.");
        }

        RECT client = default;
        GetClientRect(_window, out client);

        int width    = client.right - client.left - Margin * 2;
        int barTop   = client.bottom - client.top - Margin - BarHeight;

        _text = CreateWindowEx(0, StaticClassName, string.Empty, WsChild | WsVisible | SsLeft,
            Margin, Margin, width, TextHeight, _window, nint.Zero, instance, nint.Zero);

        _bar = CreateWindowEx(0, ProgressBarClassName, string.Empty, WsChild | WsVisible | PbsSmooth,
            Margin, barTop, width, BarHeight, _window, nint.Zero, instance, nint.Zero);

        nint font = GetStockObject(DefaultGuiFont);
        SendMessage(_text, WmSetFont, font, 1);
        SendMessage(_bar, WmSetFont, font, 1);

        ShowWindow(_window, SwShow);
        UpdateWindow(_window);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmClose:
                DestroyWindow(hWnd);
                return nint.Zero;

            case WmDestroy:
                PostQuitMessage(0);
                return nint.Zero;

            case WmCtlColorStatic:
                SetBkMode(wParam, Transparent);
                return GetSysColorBrush(ColorFaceIndex);
        }

        return DefWindowProc(hWnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INITCOMMONCONTROLSEX
    {
        public uint dwSize;
        public uint dwICC;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial nint GetModuleHandle(string? lpModuleName);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static partial ushort RegisterClassEx(in WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial nint CreateWindowEx(uint dwExStyle, string? lpClassName, string? lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    private static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static partial int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(in MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowText(nint hWnd, string lpString);

    [LibraryImport("user32.dll", EntryPoint = "GetClientRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static partial nint LoadCursor(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint hWnd);

    [LibraryImport("gdi32.dll", EntryPoint = "GetStockObject")]
    private static partial nint GetStockObject(int i);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkMode")]
    private static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("user32.dll", EntryPoint = "GetSysColorBrush")]
    private static partial nint GetSysColorBrush(int nIndex);

    [LibraryImport("comctl32.dll", EntryPoint = "InitCommonControlsEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX picce);
}
