using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Romestead.MapWorkshop;

/// <summary>
/// Composites a TMX map into a Bitmap by stamping tiles from each referenced
/// tileset and overlaying any image layers. Handles CSV and base64+zlib/gzip
/// encodings. Returns null when the map can't be rendered (missing tilesets,
/// unsupported encoding, parse errors); the caller can fall back to whatever.
/// </summary>
internal static class TmxRenderer
{
    private const uint FlipHorizontal = 0x80000000u;
    private const uint FlipVertical   = 0x40000000u;
    private const uint FlipDiagonal   = 0x20000000u;
    private const uint GidMask        = ~(FlipHorizontal | FlipVertical | FlipDiagonal);

    public static Bitmap? Render(string tmxPath, Action<string>? warn = null)
    {
        try
        {
            return RenderCore(tmxPath, warn ?? (_ => { }));
        }
        catch (Exception ex)
        {
            warn?.Invoke($"preview render failed: {ex.Message}");
            return null;
        }
    }

    private static Bitmap? RenderCore(string tmxPath, Action<string> warn)
    {
        if (!File.Exists(tmxPath)) return null;

        var doc = XDocument.Load(tmxPath);
        var map = doc.Root;
        if (map == null || map.Name.LocalName != "map") return null;

        int mapW = (int?)map.Attribute("width") ?? 0;
        int mapH = (int?)map.Attribute("height") ?? 0;
        int tileW = (int?)map.Attribute("tilewidth") ?? 0;
        int tileH = (int?)map.Attribute("tileheight") ?? 0;
        if (mapW <= 0 || mapH <= 0 || tileW <= 0 || tileH <= 0) return null;

        int pxW = mapW * tileW;
        int pxH = mapH * tileH;
        // Sanity cap so a runaway map doesn't blow out memory in the preview pane.
        if (pxW > 4096 || pxH > 4096)
        {
            warn($"map too large for preview ({pxW}x{pxH})");
            return null;
        }

        var tilesets = LoadTilesets(map, tmxPath, warn);

        var bmp = new Bitmap(pxW, pxH);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = CompositingMode.SourceOver;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Color.FromArgb(20, 20, 24));

            // Layers + image layers in document order so the visual stacking matches Tiled.
            foreach (var node in map.Elements())
            {
                switch (node.Name.LocalName)
                {
                    case "layer":
                        if ((string?)node.Attribute("visible") == "0") break;
                        DrawTileLayer(g, node, mapW, mapH, tileW, tileH, tilesets, warn);
                        break;
                    case "imagelayer":
                        if ((string?)node.Attribute("visible") == "0") break;
                        DrawImageLayer(g, node, tmxPath, warn);
                        break;
                    // objectgroup is skipped intentionally — the preview is for the tile artwork,
                    // not collision/spawn metadata.
                }
            }
        }

        foreach (var ts in tilesets) ts.Image?.Dispose();

        return bmp;
    }

    // ------------- tile data -------------

    private static void DrawTileLayer(
        Graphics g,
        XElement layer,
        int mapW, int mapH, int tileW, int tileH,
        List<TilesetRef> tilesets,
        Action<string> warn)
    {
        var data = layer.Element(XName.Get("data", layer.Name.NamespaceName));
        if (data == null) return;

        var encoding = (string?)data.Attribute("encoding") ?? "xml";
        var compression = (string?)data.Attribute("compression");
        uint[]? gids;

        switch (encoding)
        {
            case "csv":
                gids = ParseCsv(data.Value);
                break;
            case "base64":
                gids = ParseBase64(data.Value.Trim(), compression, warn);
                break;
            default:
                warn($"unsupported layer encoding: {encoding}");
                return;
        }

        if (gids == null || gids.Length < mapW * mapH) return;

        for (int row = 0; row < mapH; row++)
        {
            for (int col = 0; col < mapW; col++)
            {
                var raw = gids[row * mapW + col];
                var gid = raw & GidMask;
                if (gid == 0) continue;

                var ts = FindTileset(tilesets, (int)gid);
                if (ts == null || ts.Image == null) continue;

                int local = (int)gid - ts.FirstGid;
                if (ts.Columns <= 0) continue;
                int srcCol = local % ts.Columns;
                int srcRow = local / ts.Columns;
                var srcRect = new Rectangle(srcCol * ts.TileW, srcRow * ts.TileH, ts.TileW, ts.TileH);
                var destRect = new Rectangle(col * tileW, row * tileH, tileW, tileH);

                bool fh = (raw & FlipHorizontal) != 0;
                bool fv = (raw & FlipVertical) != 0;
                bool fd = (raw & FlipDiagonal) != 0;

                if (!fh && !fv && !fd)
                {
                    g.DrawImage(ts.Image, destRect, srcRect, GraphicsUnit.Pixel);
                }
                else
                {
                    DrawFlipped(g, ts.Image, srcRect, destRect, fh, fv, fd);
                }
            }
        }
    }

    private static void DrawFlipped(Graphics g, Image src, Rectangle srcRect, Rectangle destRect,
        bool flipH, bool flipV, bool flipD)
    {
        // Diagonal flip = rotate 90 + horizontal flip. Translate so the tile rotates
        // around its own destination origin, then draw.
        var saved = g.Save();
        try
        {
            g.TranslateTransform(destRect.X + destRect.Width / 2f, destRect.Y + destRect.Height / 2f);
            if (flipD)
            {
                g.RotateTransform(90f);
                g.ScaleTransform(1f, -1f);
            }
            if (flipH) g.ScaleTransform(-1f, 1f);
            if (flipV) g.ScaleTransform(1f, -1f);
            var local = new Rectangle(-destRect.Width / 2, -destRect.Height / 2, destRect.Width, destRect.Height);
            g.DrawImage(src, local, srcRect, GraphicsUnit.Pixel);
        }
        finally { g.Restore(saved); }
    }

    private static uint[]? ParseCsv(string text)
    {
        var parts = text.Split(new[] { ',', '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new uint[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!uint.TryParse(parts[i], out var v)) return null;
            result[i] = v;
        }
        return result;
    }

    private static uint[]? ParseBase64(string text, string? compression, Action<string> warn)
    {
        byte[] raw;
        try { raw = Convert.FromBase64String(text); }
        catch { warn("base64 decode failed"); return null; }

        byte[] bytes;
        switch (compression)
        {
            case null:
            case "":
                bytes = raw;
                break;
            case "zlib":
                bytes = Decompress(new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress));
                break;
            case "gzip":
                bytes = Decompress(new GZipStream(new MemoryStream(raw), CompressionMode.Decompress));
                break;
            default:
                warn($"unsupported layer compression: {compression}");
                return null;
        }

        if (bytes.Length % 4 != 0) return null;
        var result = new uint[bytes.Length / 4];
        for (int i = 0; i < result.Length; i++)
        {
            // TMX stores GIDs as little-endian uint32.
            result[i] = (uint)(bytes[i * 4]
                | (bytes[i * 4 + 1] << 8)
                | (bytes[i * 4 + 2] << 16)
                | (bytes[i * 4 + 3] << 24));
        }
        return result;
    }

    private static byte[] Decompress(Stream s)
    {
        using (s)
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    // ------------- tilesets -------------

    private sealed class TilesetRef
    {
        public int FirstGid;
        public int TileW;
        public int TileH;
        public int Columns;
        public Image? Image;
    }

    private static List<TilesetRef> LoadTilesets(XElement map, string tmxPath, Action<string> warn)
    {
        var list = new List<TilesetRef>();
        foreach (var ts in map.Elements(XName.Get("tileset", map.Name.NamespaceName)))
        {
            int firstGid = (int?)ts.Attribute("firstgid") ?? 0;
            var source = (string?)ts.Attribute("source");

            XElement tsRoot;
            string tsBase;

            if (!string.IsNullOrEmpty(source))
            {
                var tsxPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(tmxPath)!,
                    source.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(tsxPath))
                {
                    warn($"missing tileset: {source}");
                    continue;
                }
                tsRoot = XDocument.Load(tsxPath).Root!;
                tsBase = Path.GetDirectoryName(tsxPath)!;
            }
            else
            {
                tsRoot = ts;
                tsBase = Path.GetDirectoryName(tmxPath)!;
            }

            int tileW = (int?)tsRoot.Attribute("tilewidth") ?? 0;
            int tileH = (int?)tsRoot.Attribute("tileheight") ?? 0;
            int columns = (int?)tsRoot.Attribute("columns") ?? 0;

            var imgEl = tsRoot.Element(XName.Get("image", tsRoot.Name.NamespaceName));
            Image? img = null;
            if (imgEl != null)
            {
                var imgSrc = (string?)imgEl.Attribute("source") ?? "";
                var imgPath = Path.GetFullPath(Path.Combine(tsBase, imgSrc.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(imgPath))
                {
                    img = LoadImageNoLock(imgPath);
                    if (columns <= 0 && img != null && tileW > 0)
                        columns = img.Width / tileW;
                }
                else
                {
                    warn($"missing tileset image: {imgSrc}");
                }
            }

            list.Add(new TilesetRef
            {
                FirstGid = firstGid,
                TileW = tileW,
                TileH = tileH,
                Columns = columns,
                Image = img,
            });
        }

        // Sort descending by firstGid so FindTileset can do a simple linear scan.
        list.Sort((a, b) => b.FirstGid.CompareTo(a.FirstGid));
        return list;
    }

    private static TilesetRef? FindTileset(List<TilesetRef> tilesets, int gid)
    {
        foreach (var ts in tilesets)
            if (gid >= ts.FirstGid) return ts;
        return null;
    }

    // ------------- image layers -------------

    private static void DrawImageLayer(Graphics g, XElement layer, string tmxPath, Action<string> warn)
    {
        var img = layer.Element(XName.Get("image", layer.Name.NamespaceName));
        if (img == null) return;
        var src = (string?)img.Attribute("source");
        if (string.IsNullOrEmpty(src)) return;

        var abs = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(tmxPath)!,
            src.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(abs)) { warn($"missing imagelayer source: {src}"); return; }

        int offX = (int?)layer.Attribute("offsetx") ?? 0;
        int offY = (int?)layer.Attribute("offsety") ?? 0;

        using var image = LoadImageNoLock(abs);
        if (image != null) g.DrawImage(image, offX, offY, image.Width, image.Height);
    }

    /// <summary>
    /// Image.FromFile keeps the file locked for the lifetime of the image.
    /// FromStream demands the stream stay alive, which our 'using' wrappers
    /// would close. Copying into a fresh Bitmap detaches both.
    /// </summary>
    private static Image? LoadImageNoLock(string path)
    {
        try
        {
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var temp = Image.FromStream(fs);
            return new Bitmap(temp);
        }
        catch { return null; }
    }
}
