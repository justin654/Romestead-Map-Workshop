using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Romestead.MapWorkshop;

internal sealed record TmxInfo(
    string Path,
    int? Width,
    int? Height,
    int? TileW,
    int? TileH,
    string? FirstImagePathAbsolute);

internal static class TmxInfoReader
{
    public static TmxInfo? Read(string tmxPath)
    {
        if (!File.Exists(tmxPath)) return null;
        string xml;
        try { xml = File.ReadAllText(tmxPath); }
        catch { return null; }

        int? w = null, h = null, tw = null, th = null;
        var mm = Regex.Match(xml,
            "<map\\b[^>]*\\bwidth=\"(\\d+)\"[^>]*\\bheight=\"(\\d+)\"[^>]*\\btilewidth=\"(\\d+)\"[^>]*\\btileheight=\"(\\d+)\"");
        if (mm.Success)
        {
            w = int.Parse(mm.Groups[1].Value);
            h = int.Parse(mm.Groups[2].Value);
            tw = int.Parse(mm.Groups[3].Value);
            th = int.Parse(mm.Groups[4].Value);
        }

        string? imgAbs = null;
        var im = Regex.Match(xml, "<image\\s+source=\"([^\"]+)\"");
        if (im.Success)
        {
            var rel = im.Groups[1].Value.Replace('/', System.IO.Path.DirectorySeparatorChar);
            var abs = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(tmxPath)!, rel));
            if (File.Exists(abs)) imgAbs = abs;
        }

        return new TmxInfo(tmxPath, w, h, tw, th, imgAbs);
    }
}

/// <summary>
/// Validates a .tmx for the path conventions Romestead expects in mod redirects.
/// Port of the old map-workshop.ps1 'validate' command.
/// </summary>
internal static class TmxValidator
{
    public sealed record Result(string Path, List<string> Errors, List<string> Warnings);

    public static Result Validate(string tmxPath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var xml = File.ReadAllText(tmxPath);

        if (Regex.IsMatch(xml, "source=\"[^\"]*Content/tilesets/"))
            errors.Add("Tileset source contains Content/tilesets/ - use ../../../tilesets/<name>.tsx.");

        foreach (Match m in Regex.Matches(xml, "source=\"([^\"]+\\.tsx)\""))
        {
            var src = m.Groups[1].Value;
            if (!Regex.IsMatch(src, "^\\.\\./.*/tilesets/[^/]+\\.tsx$"))
                warnings.Add($"Tileset source '{src}' - prefer ../../../tilesets/<name>.tsx like vanilla dungeon maps.");
        }

        foreach (Match m in Regex.Matches(xml, "<image\\s+source=\"([^\"]+)\""))
        {
            var src = m.Groups[1].Value;
            if (src.Contains("Content/"))
                errors.Add("Image source must not include Content/ - use ../../media/... like vanilla building maps.");
        }

        if (!Regex.IsMatch(xml, "<tileset\\s"))
            warnings.Add("No tileset - OK for image-only building interiors (e.g. vanilla Insula).");

        return new Result(tmxPath, errors, warnings);
    }
}
