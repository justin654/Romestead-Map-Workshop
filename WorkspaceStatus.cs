using System.IO;
using System.Text.RegularExpressions;

namespace Romestead.MapWorkshop;

internal readonly record struct WorkspaceStatus(
    bool Ripped,
    int XnbPending,
    int TsxBroken,
    int TsxTotal,
    string TiledExe)
{
    public static WorkspaceStatus Probe()
    {
        var ripped = Directory.Exists(Paths.RippedMaps);
        var tiled = Paths.FindTiledExe();

        if (!ripped)
            return new WorkspaceStatus(false, 0, 0, 0, tiled);

        int tsxTotal = 0, tsxBroken = 0;
        if (Directory.Exists(Paths.RippedTilesets))
        {
            foreach (var tsx in Directory.EnumerateFiles(Paths.RippedTilesets, "*.tsx", SearchOption.TopDirectoryOnly))
            {
                tsxTotal++;
                var xml = File.ReadAllText(tsx);
                var m = Regex.Match(xml, "<image\\s+source=\"([^\"]+)\"");
                if (!m.Success) continue;
                var rel = m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
                var abs = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(tsx)!, rel));
                if (!File.Exists(abs)) tsxBroken++;
            }
        }

        int xnbPending = 0;
        foreach (var sub in new[] { Path.Combine("media", "tiles"), Path.Combine("media", "map_backgrounds") })
        {
            var dir = Path.Combine(Paths.RippedRoot, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var xnb in Directory.EnumerateFiles(dir, "*.xnb", SearchOption.AllDirectories))
            {
                var png = Path.ChangeExtension(xnb, ".png");
                if (!File.Exists(png)) xnbPending++;
            }
        }

        return new WorkspaceStatus(true, xnbPending, tsxBroken, tsxTotal, tiled);
    }
}
