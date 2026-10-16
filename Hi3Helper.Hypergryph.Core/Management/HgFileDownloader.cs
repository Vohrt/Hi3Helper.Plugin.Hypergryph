using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Hi3Helper.Plugin.Core;
using Microsoft.Extensions.Logging;

namespace Hi3Helper.Hypergryph.Core.Management;

internal static class HgFileDownloader
{
    public static void ForceDeleteFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }
        catch
        {
        }

        File.Delete(filePath);
    }

    public static async Task DownloadFileAsync(HttpClient client, string url, string tempPath, long expectedSize,
        CancellationToken token, Action<long> onProgress)
    {
        long totalReported = 0;
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
            try
            {
                long existingLength = 0;
                if (File.Exists(tempPath))
                {
                    existingLength = new FileInfo(tempPath).Length;
                    if (existingLength > expectedSize)
                    {
                        ForceDeleteFile(tempPath);
                        existingLength = 0;
                    }
                }

                var diff = existingLength - totalReported;
                if (diff != 0)
                {
                    onProgress(diff);
                    totalReported += diff;
                }

                if (existingLength == expectedSize) return;

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (existingLength > 0)
                    request.Headers.Range = new RangeHeaderValue(existingLength, null);

                using var response = await client.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

                if (existingLength > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    existingLength = 0;
                    ForceDeleteFile(tempPath);
                    if (totalReported > 0)
                    {
                        onProgress(-totalReported);
                        totalReported = 0;
                    }
                }

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var fs = new FileStream(tempPath,
                    existingLength > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true);

                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    onProgress(read);
                    totalReported += read;
                }

                await fs.FlushAsync(token).ConfigureAwait(false);
                if (fs.Length != expectedSize)
                    throw new Exception($"Size mismatch. Expected {expectedSize}, Got {fs.Length}");

                return;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                    throw new Exception(
                        $"Download failed after {maxRetries} attempts for {url} | Error: {ex.Message}");

                SharedStatic.InstanceLogger.LogWarning(
                    $"[HgDownload] Interrupted, retrying ({attempt}/{maxRetries}) for {Path.GetFileName(url)}...");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
    }

    public static async Task<bool> CheckMd5Async(string filePath, string expectedMd5, CancellationToken token)
    {
        if (!File.Exists(filePath)) return false;

        using var md5 = MD5.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await md5.ComputeHashAsync(stream, token).ConfigureAwait(false);
        return BitConverter.ToString(hashBytes).Replace("-", "")
            .Equals(expectedMd5, StringComparison.OrdinalIgnoreCase);
    }
}
