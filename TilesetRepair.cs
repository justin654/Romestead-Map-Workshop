using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Romestead.MapWorkshop;

/// <summary>
/// Fixes .tsx image references in the ripped Content tree so Tiled can find
/// tileset PNGs. Equivalent to the old repair-tiled-paths.ps1.
/// </summary>
internal static class TilesetRepair
{
    public static Task<(int tsxFixed, int xnbConverted)> RepairAsync(IProgressSink sink, bool allowGameInstall = false) =>
        Task.Run(() => Repair(sink, allowGameInstall), sink.CancellationToken);

    private static (int tsxFixed, int xnbConverted) Repair(IProgressSink sink, bool allowGameInstall)
    {
        var root = Paths.RippedRoot;
        if (!allowGameInstall &&
            Path.GetFullPath(root).Equals(Path.GetFullPath(Paths.GameContent), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to modify game Content. Operate on workspace/ripped/Content instead.");
        }

        // Flatten accidental Content/Content nesting.
        var nested = Path.Combine(root, "Content");
        if (Directory.Exists(Path.Combine(nested, "maps")) && !Directory.Exists(Path.Combine(root, "maps")))
        {
            sink.Log($"Using nested root: {nested}");
            root = nested;
        }

        var tilesetsDir = Path.Combine(root, "tilesets");
        if (!Directory.Exists(tilesetsDir))
            throw new DirectoryNotFoundException($"No tilesets folder under {root}");

        int tsxFixed = 0, xnbConverted = 0;

        foreach (var tsx in Directory.EnumerateFiles(tilesetsDir, "*.tsx", SearchOption.TopDirectoryOnly))
        {
            sink.CancellationToken.ThrowIfCancellationRequested();

            var xml = File.ReadAllText(tsx);
            var m = Regex.Match(xml, "<image\\s+source=\"([^\"]+)\"");
            if (!m.Success) continue;

            var originalSource = m.Groups[1].Value;
            var rel = originalSource.Replace('/', Path.DirectorySeparatorChar);
            var expectedPng = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(tsx)!, rel));

            if (File.Exists(expectedPng)) continue;

            var fileName = Path.GetFileName(expectedPng);
            var altName = Path.GetFileNameWithoutExtension(fileName);

            // Look in media/tiles/PNGs/<file>.png (the LeonBlade default extraction layout).
            var pngsCandidate = Path.Combine(root, "media", "tiles", "PNGs", fileName);
            if (!File.Exists(pngsCandidate))
            {
                var pngsDir = Path.Combine(root, "media", "tiles", "PNGs");
                if (Directory.Exists(pngsDir))
                {
                    pngsCandidate = Directory.EnumerateFiles(pngsDir, altName + ".png", SearchOption.TopDirectoryOnly)
                                             .FirstOrDefault() ?? "";
                }
                else { pngsCandidate = ""; }
            }

            if (!string.IsNullOrEmpty(pngsCandidate) && File.Exists(pngsCandidate))
            {
                var newSource = "../media/tiles/PNGs/" + fileName;
                var newXml = xml.Replace(originalSource, newSource);
                if (newXml != xml)
                {
                    File.WriteAllText(tsx, newXml);
                    sink.Log($"  tsx: {Path.GetFileName(tsx)} -> {newSource}");
                    tsxFixed++;
                }
                continue;
            }

            // Fall back to xnb-converting the original file alongside the expected PNG.
            var xnbCandidate = Path.ChangeExtension(expectedPng, ".xnb");
            if (!File.Exists(xnbCandidate))
                xnbCandidate = Path.Combine(root, "media", "tiles", altName + ".xnb");

            if (File.Exists(xnbCandidate) && XnbConverter.IsInstalled)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(expectedPng)!);
                var psi = new ProcessStartInfo
                {
                    FileName = Paths.XnbCliExe,
                    Arguments = $"unpack {ProcessRunner.Quote(xnbCandidate)} {ProcessRunner.Quote(Path.GetDirectoryName(expectedPng)!)}",
                    WorkingDirectory = Path.GetDirectoryName(Paths.XnbCliExe),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi)!;
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (File.Exists(expectedPng))
                {
                    sink.Log($"  xnb->png: {fileName}");
                    xnbConverted++;
                }
            }
        }

        sink.Log("");
        sink.Log($"Repair complete. tsx paths fixed: {tsxFixed}  xnb converted: {xnbConverted}");
        return (tsxFixed, xnbConverted);
    }
}
