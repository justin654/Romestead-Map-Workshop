using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Romestead.MapWorkshop;

/// <summary>
/// Locates the Romestead game install. Tries (in order): saved config, Steam
/// registry + libraryfolders.vdf, then a folder-picker dialog.
/// </summary>
internal static class GameFolderResolver
{
    private const string GameExeName = "Romestead.exe";
    private const string SteamRelative = "steamapps/common/romestead";

    /// <summary>
    /// Returns a valid path to the Romestead install directory, or null if the
    /// user cancels the picker.
    /// </summary>
    public static string? Resolve(AppConfig config)
    {
        // 1. Saved config.
        if (!string.IsNullOrEmpty(config.GameRoot) && IsValidGameRoot(config.GameRoot))
            return config.GameRoot;

        // 2. Auto-detect via Steam libraries, ask user to confirm.
        var auto = FindViaSteam();
        if (auto != null)
        {
            var answer = MessageBox.Show(
                $"Found Romestead at:\r\n\r\n{auto}\r\n\r\nUse this folder?",
                "Map Workshop - first-run setup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                config.GameRoot = auto;
                config.Save();
                return auto;
            }
        }

        // 3. Manual picker.
        while (true)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select the Romestead game folder (contains Romestead.exe)",
                ShowNewFolderButton = false,
                UseDescriptionForTitle = true,
            };
            if (dlg.ShowDialog() != DialogResult.OK) return null;
            if (IsValidGameRoot(dlg.SelectedPath))
            {
                config.GameRoot = dlg.SelectedPath;
                config.Save();
                return dlg.SelectedPath;
            }

            var retry = MessageBox.Show(
                $"That folder doesn't look like a Romestead install ({GameExeName} not found).\r\n\r\nPick a different folder?",
                "Map Workshop",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (retry != DialogResult.Yes) return null;
        }
    }

    public static bool IsValidGameRoot(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!Directory.Exists(path)) return false;
        return File.Exists(Path.Combine(path, GameExeName));
    }

    // ---------- Steam discovery ----------

    private static string? FindViaSteam()
    {
        foreach (var lib in EnumerateSteamLibraries())
        {
            var candidate = Path.Combine(lib, SteamRelative.Replace('/', Path.DirectorySeparatorChar));
            if (IsValidGameRoot(candidate)) return candidate;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateSteamLibraries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamRoot = GetSteamInstallPath();
        if (steamRoot == null) yield break;

        if (seen.Add(steamRoot)) yield return steamRoot;

        // Steam stores a list of extra libraries in libraryfolders.vdf.
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        string text;
        try { text = File.ReadAllText(vdf); }
        catch { yield break; }

        // Match  "path"   "C:\\Steam\\..."  entries. Steam writes paths with
        // doubled backslashes.
        foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
        {
            var raw = m.Groups[1].Value.Replace("\\\\", "\\");
            if (seen.Add(raw) && Directory.Exists(raw))
                yield return raw;
        }
    }

    private static string? GetSteamInstallPath()
    {
        // Per-user install.
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (k?.GetValue("SteamPath") is string p && Directory.Exists(p)) return p;
        }
        catch { }

        // 32/64-bit machine install.
        foreach (var path in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(path);
                if (k?.GetValue("InstallPath") is string p && Directory.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }
}
