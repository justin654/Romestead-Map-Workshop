using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Romestead.MapWorkshop;

/// <summary>
/// High-level orchestration backing the Rip / Prepare / Open in Tiled buttons.
/// Pack-and-sync is intentionally absent from this standalone build until the
/// Romestead mod loader is released for public use.
/// </summary>
internal static class Operations
{
    public static async Task<bool> EnsurePreparedAsync(IProgressSink sink, bool force = false)
    {
        if (!Directory.Exists(Paths.RippedMaps))
        {
            sink.Log("Cannot prepare: ripped Content missing. Click 'Rip game Content' first.");
            return false;
        }

        var status = WorkspaceStatus.Probe();
        var needsXnb = force || status.XnbPending > 0;
        var needsRepair = force || status.TsxBroken > 0;

        if (needsXnb)
        {
            if (!XnbConverter.IsInstalled)
            {
                sink.Log("xnbcli.exe missing - installing...");
                try
                {
                    await XnbConverter.InstallAsync(sink).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    sink.Log("xnbcli install failed: " + ex.Message);
                    return false;
                }
            }

            try
            {
                await XnbConverter.ConvertMediaAsync(sink).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                sink.Log("xnb conversion error: " + ex.Message);
                // continue to repair - still useful
            }
        }

        if (needsRepair)
        {
            try
            {
                await TilesetRepair.RepairAsync(sink).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                sink.Log("repair error: " + ex.Message);
                return false;
            }
        }

        return true;
    }

    public static async Task OpenInTiledAsync(string tmxPath, IProgressSink sink)
    {
        if (string.IsNullOrEmpty(tmxPath) || !File.Exists(tmxPath))
        {
            sink.Log($"TMX not found: {tmxPath}");
            return;
        }

        if (!await EnsurePreparedAsync(sink).ConfigureAwait(false))
            return;

        var tiled = Paths.FindTiledExe();
        if (string.IsNullOrEmpty(tiled))
        {
            sink.Log("Tiled.exe not found. Install from https://www.mapeditor.org/ then click Refresh.");
            return;
        }

        sink.Log($"Opening in Tiled: {tmxPath}");
        Process.Start(new ProcessStartInfo
        {
            FileName = tiled,
            Arguments = ProcessRunner.Quote(tmxPath),
            UseShellExecute = false,
        });
    }
}
