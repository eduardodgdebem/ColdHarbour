using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ColdHarbour.Application.Playback.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Playback;

public sealed class TranscodeService(IConfiguration config, ILogger<TranscodeService> logger) : ITranscodeService
{
    // One semaphore per cache key so concurrent requests for the same transcode wait;
    // requests for different transcodes run in parallel.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private static readonly Dictionary<string, (string Ext, string Args)> Profiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["opus-128"] = (".opus", "-c:a libopus -b:a 128k -vn -f opus"),
            ["aac-192"]  = (".m4a",  "-c:a aac -b:a 192k -vn -f mp4"),
            ["mp3-192"]  = (".mp3",  "-c:a libmp3lame -b:a 192k -vn -f mp3"),
        };

    private string ContentRoot => config["COLDHARBOUR_CONTENT_ROOT"]
        ?? Path.Combine(Path.GetTempPath(), "coldharbour");

    private string FfmpegPath => config["COLDHARBOUR_FFMPEG_PATH"] ?? "ffmpeg";

    public async Task<string?> GetOrTranscodeAsync(string sourcePath, string audioSha1, string profile, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            return null;

        if (!Profiles.TryGetValue(profile, out var profileDef))
            throw new ArgumentException($"Unknown transcode profile: {profile}", nameof(profile));

        var cacheKey = ComputeCacheKey(audioSha1, profile);
        var cacheDir = Path.Combine(ContentRoot, "cache", "transcodes");
        var cachePath = Path.Combine(cacheDir, cacheKey + profileDef.Ext);

        if (File.Exists(cachePath))
            return cachePath;

        var sem = Locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (File.Exists(cachePath))
                return cachePath;

            Directory.CreateDirectory(cacheDir);
            var tmpPath = cachePath + $".tmp-{Guid.NewGuid():N}";

            try
            {
                await RunFfmpegAsync(sourcePath, tmpPath, profileDef.Args, ct);
                File.Move(tmpPath, cachePath, overwrite: false);
                logger.LogInformation("Transcode complete: {CacheKey} ({Profile})", cacheKey, profile);
            }
            catch
            {
                try { File.Delete(tmpPath); } catch { /* ignore */ }
                throw;
            }

            return cachePath;
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task RunFfmpegAsync(string source, string dest, string ffmpegArgs, CancellationToken ct)
    {
        var args = $"-y -i \"{source}\" {ffmpegArgs} \"{dest}\"";
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegPath,
            Arguments = args,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        // Kill FFmpeg when the request is cancelled (tab closed, seek abort, etc.)
        await using var reg = ct.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* process may have already exited */ }
        });

        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                $"FFmpeg failed (exit {proc.ExitCode}): {stderr[..Math.Min(500, stderr.Length)]}");
        }
    }

    private static string ComputeCacheKey(string audioSha1, string profile)
    {
        var input = Encoding.UTF8.GetBytes($"{audioSha1}|{profile}");
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
