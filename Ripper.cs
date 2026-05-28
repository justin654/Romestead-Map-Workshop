using System;
using System.IO;
using System.Threading.Tasks;

namespace Romestead.MapWorkshop;

internal enum RipProfile
{
    MapAuthor,
    Interiors,
    Full,
}

/// <summary>
/// Copies the game Content tree (or a subset) into workspace/ripped/Content so
/// Tiled can resolve relative paths. Equivalent to the old rip-game-content.ps1.
/// </summary>
internal static class Ripper
{
    public static Task RipAsync(RipProfile profile, bool force, IProgressSink sink) =>
        Task.Run(() => Rip(profile, force, sink), sink.CancellationToken);

    private static void Rip(RipProfile profile, bool force, IProgressSink sink)
    {
        var dest = Paths.RippedRoot;
        var source = Paths.GameContent;

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Game Content folder not found: {source}");

        sink.Status($"Ripping ({profile})");
        sink.Log($"Ripping Content profile '{profile}' -> {dest}");
        sink.Log($"Game Content: {source}");

        if (Directory.Exists(dest))
        {
            if (!force)
            {
                sink.Log($"Output already exists; pass Force=true to refresh: {dest}");
                return;
            }
            sink.Log("Removing previous ripped tree...");
            DeleteDirectoryWithRetry(dest);
        }
        Directory.CreateDirectory(dest);

        switch (profile)
        {
            case RipProfile.MapAuthor:
                CopyTree(source, dest, "tilesets", sink);
                CopyTree(source, dest, "maps", sink);
                CopyTree(source, dest, Path.Combine("media", "tiles"), sink);
                CopyTree(source, dest, Path.Combine("media", "map_backgrounds"), sink);
                CopyTree(source, dest, "tiled-templates", sink);
                break;

            case RipProfile.Interiors:
                CopyTree(source, dest, "tilesets", sink);
                CopyTree(source, dest, Path.Combine("maps", "interiors_new"), sink);
                CopyTree(source, dest, Path.Combine("maps", "building_exteriors"), sink);
                CopyTree(source, dest, Path.Combine("media", "tiles"), sink);
                CopyTree(source, dest, Path.Combine("media", "map_backgrounds"), sink);
                break;

            case RipProfile.Full:
                // Copy children of Content/ (avoids Content/Content/ nesting).
                foreach (var child in Directory.EnumerateDirectories(source))
                {
                    var name = Path.GetFileName(child);
                    CopyTree(source, dest, name, sink);
                }
                foreach (var f in Directory.EnumerateFiles(source))
                {
                    var name = Path.GetFileName(f);
                    var d = Path.Combine(dest, name);
                    File.Copy(f, d, overwrite: true);
                }
                sink.Log("  copied: Content/* (full tree)");
                break;
        }

        WriteReadme(profile, dest);

        var counts = Count(dest);
        sink.Log("");
        sink.Log($"Rip complete. tmx={counts.tmx}  png={counts.png}  xnb={counts.xnb}");
        sink.Log($"Tiled project root: {dest}");
    }

    private static void CopyTree(string sourceRoot, string destRoot, string relative, IProgressSink sink)
    {
        var src = Path.Combine(sourceRoot, relative);
        if (!Directory.Exists(src) && !File.Exists(src))
        {
            sink.Log($"  skip (missing): {relative}");
            return;
        }

        var dst = Path.Combine(destRoot, relative);
        if (File.Exists(src))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
        else
        {
            CopyDirectoryRecursive(src, dst);
        }
        sink.Log($"  copied: {relative}");
    }

    private static void CopyDirectoryRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            File.Copy(file, Path.Combine(dst, rel), overwrite: true);
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (int i = 0; i < 4; i++)
        {
            try { Directory.Delete(path, recursive: true); return; }
            catch (IOException) when (i < 3) { System.Threading.Thread.Sleep(200); }
            catch (UnauthorizedAccessException) when (i < 3) { System.Threading.Thread.Sleep(200); }
        }
    }

    private static (int tmx, int png, int xnb) Count(string root)
    {
        int tmx = 0, png = 0, xnb = 0;
        foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(f);
            if (ext.Equals(".tmx", StringComparison.OrdinalIgnoreCase)) tmx++;
            else if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)) png++;
            else if (ext.Equals(".xnb", StringComparison.OrdinalIgnoreCase)) xnb++;
        }
        return (tmx, png, xnb);
    }

    private static void WriteReadme(RipProfile profile, string ripRoot)
    {
        var readme = Path.Combine(Path.GetDirectoryName(ripRoot)!, "README.txt");
        var contents = $@"Romestead ripped Content ({profile})
Generated: {DateTime.Now:o}

Edit maps in Tiled from this folder (paths match the game install):
  {ripRoot}\maps\...

If tileset images are missing, use Map Workshop's 'Prepare for Tiled' button or
relaunch and click 'Open in Tiled' (auto-prep will run).
";
        File.WriteAllText(readme, contents);
    }
}
