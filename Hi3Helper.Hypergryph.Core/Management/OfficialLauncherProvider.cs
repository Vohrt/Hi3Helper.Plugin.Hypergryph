using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hi3Helper.Hypergryph.Core.Management.Api;
using Hi3Helper.Plugin.Core;
using Hi3Helper.Plugin.Core.Utility;
using Microsoft.Extensions.Logging;
using SevenZipExtractor;

namespace Hi3Helper.Hypergryph.Core.Management;

public enum LauncherAcquireStatus
{
    Ready,
    Acquired,
    Unavailable
}

public readonly record struct LauncherAcquireOutcome(LauncherAcquireStatus Status, OfficialLauncherBridge? Bridge);

public static class OfficialLauncherProvider
{
    private const string LauncherExecutableName = "Games.exe";
    private const string PackageFileName        = "package.zip";
    private const string PickedLauncherFileName = "picked_launcher.txt";

    private static readonly HttpClient DownloadHttpClient = new PluginHttpClientBuilder()
        .SetAllowedDecompression(DecompressionMethods.GZip)
        .AllowCookies()
        .AllowRedirections()
        .AllowUntrustedCert()
        .Create();

    public static string GetCacheDirectory(HgGameManager manager, string profileName)
    {
        manager.GetGamePath(out string? gamePath);

        if (string.IsNullOrEmpty(gamePath))
        {
            throw new InvalidOperationException(
                "The game installation path is unset, so the launcher component cannot be stored.");
        }

        return Path.Combine(Path.GetFullPath(gamePath), "Hi3Helper", "launcher", profileName);
    }

    public static async Task<LauncherAcquireOutcome> EnsureAsync(HgGameManager manager, string profileName,
        string zoneFullName, CancellationToken token)
    {
        string cacheDirectory = GetCacheDirectory(manager, profileName);

        KillStaleCachedLauncher(cacheDirectory);

        string? pickedLauncher = ReadPickedLauncher(cacheDirectory);

        if (OfficialLauncherBridge.TryCreate(cacheDirectory, pickedLauncher, out OfficialLauncherBridge? bridge,
                out string? path))
        {
            SharedStatic.InstanceLogger.LogInformation($"[HgLauncher] Using existing official launcher: {path}");
            return new LauncherAcquireOutcome(LauncherAcquireStatus.Ready, bridge);
        }

        HgGetLatestLauncherRsp? package = await QueryPackageAsync(manager, token).ConfigureAwait(false);

        LauncherAcquireChoice choice = NativePrompt.AskAcquire(zoneFullName, GetPackageSize(package));

        switch (choice)
        {
            case LauncherAcquireChoice.Download:
                if (!await DownloadLauncherAsync(package!, cacheDirectory, token).ConfigureAwait(false))
                {
                    if (!token.IsCancellationRequested)
                    {
                        NativePrompt.NotifyDownloadFailed();
                    }

                    return new LauncherAcquireOutcome(LauncherAcquireStatus.Unavailable, null);
                }

                break;

            case LauncherAcquireChoice.PickExecutable:
                bridge = PromptForExistingLauncher(cacheDirectory);

                return bridge == null
                    ? new LauncherAcquireOutcome(LauncherAcquireStatus.Unavailable, null)
                    : new LauncherAcquireOutcome(LauncherAcquireStatus.Ready, bridge);

            default:
                SharedStatic.InstanceLogger.LogInformation("[HgLauncher] User cancelled the launcher acquisition.");
                return new LauncherAcquireOutcome(LauncherAcquireStatus.Unavailable, null);
        }

        if (OfficialLauncherBridge.TryCreate(cacheDirectory, null, out bridge, out path))
        {
            SharedStatic.InstanceLogger.LogInformation($"[HgLauncher] Downloaded launcher component: {path}");

            bridge!.Dispose();
            NativePrompt.NotifyComponentReady();

            return new LauncherAcquireOutcome(LauncherAcquireStatus.Acquired, null);
        }

        SharedStatic.InstanceLogger.LogError(
            $"[HgLauncher] Downloaded launcher component is unusable: {cacheDirectory}");
        NativePrompt.NotifyDownloadFailed();
        return new LauncherAcquireOutcome(LauncherAcquireStatus.Unavailable, null);
    }

    public static async Task<bool> TryAcquireAsync(HgGameManager manager, string profileName, CancellationToken token)
    {
        HgGetLatestLauncherRsp? package = await QueryPackageAsync(manager, token).ConfigureAwait(false);
        return package != null
               && await DownloadLauncherAsync(package, GetCacheDirectory(manager, profileName), token)
                   .ConfigureAwait(false);
    }

    private static string GetPickedLauncherFilePath(string cacheDirectory)
        => Path.Combine(cacheDirectory, PickedLauncherFileName);

    private static string? ReadPickedLauncher(string cacheDirectory)
    {
        string filePath = GetPickedLauncherFilePath(cacheDirectory);

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            string value = File.ReadAllText(filePath).Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            SharedStatic.InstanceLogger.LogWarning($"[HgLauncher] Cannot read {filePath}: {ex.Message}");
            return null;
        }
    }

    private static void SavePickedLauncher(string cacheDirectory, string executablePath)
    {
        string filePath = GetPickedLauncherFilePath(cacheDirectory);

        try
        {
            Directory.CreateDirectory(cacheDirectory);
            File.WriteAllText(filePath, executablePath);
            SharedStatic.InstanceLogger.LogInformation($"[HgLauncher] Remembered the picked launcher: {filePath}");
        }
        catch (Exception ex)
        {
            SharedStatic.InstanceLogger.LogWarning(
                $"[HgLauncher] Cannot remember the picked launcher path in {filePath}: {ex.Message}");
        }
    }

    private static async Task<HgGetLatestLauncherRsp?> QueryPackageAsync(HgGameManager manager, CancellationToken token)
    {
        HgGetLatestLauncherRsp? package = await manager.GetLatestLauncherAsync(token).ConfigureAwait(false);

        if (package != null && !string.IsNullOrEmpty(package.ZipPackageUrl))
        {
            return package;
        }

        SharedStatic.InstanceLogger.LogWarning(
            "[HgLauncher] The official launcher package is unavailable for this region.");
        return null;
    }

    private static long GetPackageSize(HgGetLatestLauncherRsp? package)
        => package != null && long.TryParse(package.PackageSize, out long size) ? size : 0;

    private static void KillStaleCachedLauncher(string cacheDirectory)
    {
        string processName = Path.GetFileNameWithoutExtension(LauncherExecutableName);

        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                string? modulePath = process.MainModule?.FileName;

                if (modulePath != null
                    && modulePath.StartsWith(cacheDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    SharedStatic.InstanceLogger.LogWarning(
                        $"[HgLauncher] Removed a leftover suspended launcher process (pid {process.Id}).");
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static OfficialLauncherBridge? PromptForExistingLauncher(string cacheDirectory)
    {
        while (true)
        {
            string? executable = NativePrompt.AskLauncherExecutable();
            if (string.IsNullOrEmpty(executable))
            {
                SharedStatic.InstanceLogger.LogInformation("[HgLauncher] User cancelled the launcher file picker.");
                return null;
            }

            if (OfficialLauncherBridge.TryCreate(null, executable, out OfficialLauncherBridge? bridge,
                    out string? path))
            {
                SavePickedLauncher(cacheDirectory, path!);
                SharedStatic.InstanceLogger.LogInformation($"[HgLauncher] Using picked official launcher: {path}");
                return bridge;
            }

            SharedStatic.InstanceLogger.LogWarning($"[HgLauncher] Not a valid official launcher: {executable}");
            NativePrompt.WarnInvalidLauncher(executable);
        }
    }

    private static async Task<bool> DownloadLauncherAsync(HgGetLatestLauncherRsp package, string cacheDirectory,
        CancellationToken token)
    {
        if (!long.TryParse(package.PackageSize, out long expectedSize) || expectedSize <= 0)
        {
            SharedStatic.InstanceLogger.LogError("[HgLauncher] Launcher package size is missing or invalid.");
            return false;
        }

        Directory.CreateDirectory(cacheDirectory);

        string packagePath = Path.Combine(cacheDirectory, PackageFileName);
        string tempPath    = packagePath + ".tmp";

        using ProgressPrompt progress = ProgressPrompt.Start(PromptStrings.Current.ProgressCaption);

        bool packageReady = false;

        if (File.Exists(packagePath) && new FileInfo(packagePath).Length == expectedSize)
        {
            progress.Report(LauncherProgressStage.Verifying, 0);

            packageReady = string.IsNullOrEmpty(package.Md5)
                           || await HgFileDownloader.CheckMd5Async(packagePath, package.Md5, token)
                               .ConfigureAwait(false);

            if (!packageReady)
            {
                HgFileDownloader.ForceDeleteFile(packagePath);
            }
        }

        if (!packageReady)
        {
            SharedStatic.InstanceLogger.LogInformation(
                $"[HgLauncher] Downloading launcher component ({expectedSize / 1024d / 1024d:F1} MB) into {cacheDirectory}...");

            progress.Report(LauncherProgressStage.Downloading, 0);

            long downloaded   = 0;
            long logThreshold = expectedSize / 10;
            long nextLogAt    = logThreshold;
            int lastPercent   = 0;

            await HgFileDownloader.DownloadFileAsync(DownloadHttpClient, package.ZipPackageUrl!, tempPath,
                expectedSize, token, delta =>
                {
                    long current = Interlocked.Add(ref downloaded, delta);
                    int percent  = (int)Math.Clamp(current * 100 / expectedSize, 0, 100);

                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress.Report(LauncherProgressStage.Downloading, percent);
                    }

                    if (logThreshold > 0 && current >= nextLogAt)
                    {
                        nextLogAt += logThreshold;
                        SharedStatic.InstanceLogger.LogInformation(
                            $"[HgLauncher] Download progress: {percent}%");
                    }
                }).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(package.Md5))
            {
                progress.Report(LauncherProgressStage.Verifying, 100);

                if (!await HgFileDownloader.CheckMd5Async(tempPath, package.Md5, token).ConfigureAwait(false))
                {
                    HgFileDownloader.ForceDeleteFile(tempPath);
                    SharedStatic.InstanceLogger.LogError("[HgLauncher] Launcher package MD5 mismatch.");
                    return false;
                }
            }

            HgFileDownloader.ForceDeleteFile(packagePath);
            File.Move(tempPath, packagePath, true);
        }

        string launcherPath = Path.Combine(cacheDirectory, LauncherExecutableName);
        HgFileDownloader.ForceDeleteFile(launcherPath);

        progress.Report(LauncherProgressStage.Extracting, 100);

        SharedStatic.InstanceLogger.LogInformation("[HgLauncher] Extracting launcher executable...");

        using (ArchiveFile archive = new(packagePath))
        {
            await archive.ExtractAsync(entry =>
                string.Equals(Path.GetFileName(entry.FileName), LauncherExecutableName,
                    StringComparison.OrdinalIgnoreCase)
                    ? launcherPath
                    : null, true, token).ConfigureAwait(false);
        }

        if (!File.Exists(launcherPath))
        {
            SharedStatic.InstanceLogger.LogError(
                $"[HgLauncher] {LauncherExecutableName} was not found inside the launcher package.");
            return false;
        }

        HgFileDownloader.ForceDeleteFile(packagePath);
        HgFileDownloader.ForceDeleteFile(tempPath);
        return true;
    }
}
