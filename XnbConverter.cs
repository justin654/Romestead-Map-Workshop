using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Romestead.MapWorkshop;

/// <summary>
/// Wraps xnbcli.exe to convert .xnb textures to .png. Also installs xnbcli
/// from GitHub when missing.
/// </summary>
internal static class XnbConverter
{
    private const string XnbCliReleaseUrl =
        "https://github.com/LeonBlade/xnbcli/releases/download/v1.0.7/xnbcli-windows-x64.zip";

    public static bool IsInstalled => File.Exists(Paths.XnbCliExe);

    public static async Task InstallAsync(IProgressSink sink)
    {
        sink.Status("Downloading xnbcli v1.0.7");
        sink.Log($"Downloading xnbcli v1.0.7 -> {Paths.XnbCliDir}");

        Directory.CreateDirectory(Paths.XnbCliDir);

        var zipPath = Path.Combine(Path.GetTempPath(), $"xnbcli-{Guid.NewGuid():N}.zip");
        var stagingDir = Path.Combine(Path.GetTempPath(), $"xnbcli-extract-{Guid.NewGuid():N}");
        try
        {
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromMinutes(5);
                using var resp = await http.GetAsync(XnbCliReleaseUrl, HttpCompletionOption.ResponseHeadersRead, sink.CancellationToken).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                using var fs = File.Create(zipPath);
                await resp.Content.CopyToAsync(fs, sink.CancellationToken).ConfigureAwait(false);
            }
            sink.Log("Extracting archive...");
            ZipFile.ExtractToDirectory(zipPath, stagingDir);

            var exe = Directory.EnumerateFiles(stagingDir, "xnbcli.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe == null)
                throw new InvalidOperationException("xnbcli.exe not found inside the release zip.");

            var srcRoot = Path.GetDirectoryName(exe)!;
            foreach (var file in Directory.EnumerateFiles(srcRoot))
            {
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(Paths.XnbCliDir, name), overwrite: true);
            }

            sink.Log($"Installed: {Paths.XnbCliExe}");
        }
        finally
        {
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); } catch { }
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    /// <summary>
    /// Convert every .xnb under media/tiles + media/map_backgrounds that lacks
    /// a sibling .png. Returns counts of converted/skipped/failed.
    /// </summary>
    public static async Task<(int converted, int skipped, int failed)> ConvertMediaAsync(IProgressSink sink)
    {
        if (!IsInstalled)
            throw new FileNotFoundException("xnbcli.exe is missing. Install it first.", Paths.XnbCliExe);

        var xnbFiles = new List<string>();
        foreach (var sub in new[] { Path.Combine("media", "tiles"), Path.Combine("media", "map_backgrounds") })
        {
            var dir = Path.Combine(Paths.RippedRoot, sub);
            if (Directory.Exists(dir))
                xnbFiles.AddRange(Directory.EnumerateFiles(dir, "*.xnb", SearchOption.AllDirectories));
        }

        if (xnbFiles.Count == 0)
        {
            sink.Log("No .xnb files under media/tiles or media/map_backgrounds.");
            return (0, 0, 0);
        }

        sink.Status($"Converting XNB (0/{xnbFiles.Count})");
        sink.Log($"Converting {xnbFiles.Count} file(s) with xnbcli...");

        int converted = 0, skipped = 0, failed = 0;
        int i = 0;
        foreach (var xnb in xnbFiles)
        {
            sink.CancellationToken.ThrowIfCancellationRequested();
            i++;
            var outDir = Path.GetDirectoryName(xnb)!;
            var baseName = Path.GetFileNameWithoutExtension(xnb);
            var outPng = Path.Combine(outDir, baseName + ".png");

            if (File.Exists(outPng))
            {
                skipped++;
                continue;
            }

            var args = $"unpack {ProcessRunner.Quote(xnb)} {ProcessRunner.Quote(outDir)}";
            var rc = await ProcessRunner.RunAsync(
                Paths.XnbCliExe,
                args,
                Path.GetDirectoryName(Paths.XnbCliExe),
                new DelegatingSink(_ => { }, _ => { }, sink.CancellationToken), // suppress xnbcli's per-file output
                quietStdout: true).ConfigureAwait(false);

            if (rc == 0 && File.Exists(outPng))
            {
                converted++;
                // The sidecar .json is only useful for repacking; remove for cleanliness.
                var sidecar = Path.Combine(outDir, baseName + ".json");
                if (File.Exists(sidecar))
                    try { File.Delete(sidecar); } catch { }
            }
            else
            {
                failed++;
                var rel = Path.GetRelativePath(Paths.RippedRoot, xnb);
                sink.Log($"  FAIL: {rel}");
            }

            if (i % 50 == 0)
            {
                sink.Status($"Converting XNB ({i}/{xnbFiles.Count})");
                sink.Log($"  progress {i} / {xnbFiles.Count}...");
            }
        }

        sink.Log($"Done. Converted: {converted}  Skipped: {skipped}  Failed: {failed}");
        return (converted, skipped, failed);
    }
}
