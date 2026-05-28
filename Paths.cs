using System;
using System.IO;

namespace Romestead.MapWorkshop;

/// <summary>
/// Path discovery for the standalone tool. The game folder is resolved at
/// startup (via GameFolderResolver) and stored in AppConfig; everything else
/// derives from that or from %LOCALAPPDATA% so we never need to write inside
/// the Steam install.
/// </summary>
internal static class Paths
{
    private static string? _gameRoot;

    /// <summary>Must be called once at startup with the resolved game folder.</summary>
    public static void SetGameRoot(string gameRoot)
    {
        _gameRoot = gameRoot;
    }

    /// <summary>Romestead install directory (contains Romestead.exe).</summary>
    public static string GameRoot
    {
        get
        {
            if (_gameRoot == null)
                throw new InvalidOperationException("GameRoot has not been resolved yet.");
            return _gameRoot;
        }
    }

    public static string GameContent => Path.Combine(GameRoot, "Content");

    /// <summary>%LOCALAPPDATA%\Romestead.MapWorkshop\ - all user-writable state.</summary>
    public static string AppDataDir => AppConfig.ConfigDir;

    /// <summary>%LOCALAPPDATA%\Romestead.MapWorkshop\workspace\ripped\Content - the ripped tree Tiled opens.</summary>
    public static string Workspace      => Path.Combine(AppDataDir, "workspace");
    public static string RippedRoot     => Path.Combine(Workspace, "ripped", "Content");
    public static string RippedMaps     => Path.Combine(RippedRoot, "maps");
    public static string RippedTilesets => Path.Combine(RippedRoot, "tilesets");

    /// <summary>Where xnbcli is downloaded to.</summary>
    public static string XnbCliDir => Path.Combine(AppDataDir, "tools", "xnbcli");
    public static string XnbCliExe => Path.Combine(XnbCliDir, "xnbcli.exe");

    public static string FindTiledExe()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tiled", "Tiled.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tiled", "Tiled.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tiled", "Tiled.exe"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return string.Empty;
    }
}
