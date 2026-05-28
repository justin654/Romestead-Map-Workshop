using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Romestead.MapWorkshop;

internal sealed class MainForm : Form
{
    private readonly AppConfig _config;

    // Top status pills
    private readonly Label _lightRip   = NewPill();
    private readonly Label _lightXnb   = NewPill();
    private readonly Label _lightTsx   = NewPill();
    private readonly Label _lightTiled = NewPill();

    // Top action row
    private readonly ComboBox _profileCombo = new();
    private readonly Button _btnRip = new() { Text = "Rip game Content" };
    private readonly Button _btnPrepare = new() { Text = "Prepare for Tiled" };
    private readonly Button _btnOpenWorkspace = new() { Text = "Open ripped folder" };
    private readonly Button _btnRefresh = new() { Text = "Refresh" };
    private readonly Button _btnChangeGame = new() { Text = "Game folder..." };

    // Left
    private readonly TextBox _filterBox = new();
    private readonly TreeView _mapTree = new();

    // Right
    private readonly PictureBox _previewBox = new();
    private readonly Label _previewMeta = new();
    private readonly TextBox _tmxBox = new();
    private readonly Button _btnBrowseTmx = new() { Text = "..." };
    private readonly Button _btnTiled = new() { Text = "Open in Tiled" };
    private readonly Button _btnValidate = new() { Text = "Validate paths" };
    private readonly Button _btnInstallTiled = new() { Text = "Where is Tiled?" };

    // Bottom
    private readonly ProgressBar _progress = new();
    private readonly Label _statusLabel = new()
    {
        Text = "Ready",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0),
    };
    private readonly TextBox _logBox = new();

    private readonly List<Button> _actionButtons;
    private CancellationTokenSource? _runningCts;

    public MainForm(AppConfig config)
    {
        _config = config;

        Text = $"Romestead Map Workshop  -  {Paths.GameRoot}";
        ClientSize = new Size(1080, 760);
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(28, 28, 32);
        ForeColor = Color.FromArgb(232, 230, 227);
        Font = new Font("Segoe UI", 9f);

        _actionButtons = new List<Button>
        {
            _btnRip, _btnPrepare, _btnOpenWorkspace, _btnRefresh, _btnChangeGame,
            _btnTiled, _btnValidate, _btnBrowseTmx, _btnInstallTiled,
        };

        BuildLayout();
        WireEvents();

        _ = RefreshStatusAndTreeAsync();

        Log("Map Workshop ready.");
        Log($"  Game:     {Paths.GameRoot}");
        Log($"  Workspace:{Paths.Workspace}");
        var tiled = Paths.FindTiledExe();
        Log(string.IsNullOrEmpty(tiled)
            ? "  Tiled:    NOT FOUND (install from mapeditor.org)"
            : $"  Tiled:    {tiled}");
        Log("");
        Log("Tip: pick a map on the left, then 'Open in Tiled'. Prep runs automatically.");
    }

    // ---------- Layout ----------

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Color.FromArgb(22, 22, 26) };
        Controls.Add(header);

        header.Controls.Add(new Label
        {
            Text = "Map Workshop",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Location = new Point(14, 8),
            Size = new Size(280, 24),
            ForeColor = Color.FromArgb(240, 240, 245),
        });
        header.Controls.Add(new Label
        {
            Text = "Rip game Content  ->  XNB to PNG  ->  Edit in Tiled",
            Location = new Point(16, 34),
            Size = new Size(600, 16),
            ForeColor = Color.FromArgb(140, 140, 150),
        });

        SetPill(_lightRip,   16,  60, 130);
        SetPill(_lightXnb,   150, 60, 160);
        SetPill(_lightTsx,   314, 60, 160);
        SetPill(_lightTiled, 478, 60, 140);
        header.Controls.Add(_lightRip);
        header.Controls.Add(_lightXnb);
        header.Controls.Add(_lightTsx);
        header.Controls.Add(_lightTiled);

        StyleButton(_btnRefresh, new Point(624, 60), new Size(70, 22), Color.FromArgb(50, 50, 58));
        header.Controls.Add(_btnRefresh);

        header.Controls.Add(new Label
        {
            Text = "Profile:",
            Location = new Point(700, 10),
            Size = new Size(50, 18),
            ForeColor = Color.FromArgb(180, 180, 195),
        });

        _profileCombo.Items.AddRange(new object[] { "MapAuthor", "Interiors", "Full" });
        _profileCombo.SelectedIndex = 0;
        _profileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileCombo.Location = new Point(752, 7);
        _profileCombo.Size = new Size(120, 22);
        _profileCombo.BackColor = Color.FromArgb(40, 40, 46);
        _profileCombo.ForeColor = Color.White;
        _profileCombo.FlatStyle = FlatStyle.Flat;
        header.Controls.Add(_profileCombo);

        StyleButton(_btnRip, new Point(880, 6), new Size(140, 24), Color.FromArgb(60, 90, 140));
        header.Controls.Add(_btnRip);

        StyleButton(_btnPrepare,       new Point(700, 36), new Size(140, 22), Color.FromArgb(70, 110, 90));
        StyleButton(_btnOpenWorkspace, new Point(848, 36), new Size(140, 22), Color.FromArgb(50, 50, 58));
        header.Controls.Add(_btnPrepare);
        header.Controls.Add(_btnOpenWorkspace);

        // Bottom (log + progress + status)
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 220, BackColor = Color.FromArgb(22, 22, 26) };
        Controls.Add(bottom);

        _progress.Dock = DockStyle.Top;
        _progress.Height = 6;
        _progress.Style = ProgressBarStyle.Continuous;
        bottom.Controls.Add(_progress);

        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 22, BackColor = Color.FromArgb(18, 18, 22) };
        _statusLabel.ForeColor = Color.FromArgb(160, 200, 160);
        statusBar.Controls.Add(_statusLabel);
        bottom.Controls.Add(statusBar);

        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Dock = DockStyle.Fill;
        _logBox.MaxLength = 0;
        _logBox.WordWrap = false;
        _logBox.BackColor = Color.FromArgb(18, 18, 22);
        _logBox.ForeColor = Color.FromArgb(200, 220, 200);
        _logBox.Font = new Font("Consolas", 9f);
        _logBox.BorderStyle = BorderStyle.None;
        bottom.Controls.Add(_logBox);
        _logBox.BringToFront();

        // Center split. Must be at the TOP of the form's z-order so WinForms'
        // reverse-order dock layout processes Fill *last* and shrinks it to the
        // remaining space (not the entire form).
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 28, 32),
            FixedPanel = FixedPanel.Panel1,
        };
        Controls.Add(split);
        split.BringToFront();
        split.Panel1.BackColor = Color.FromArgb(24, 24, 28);
        split.Panel2.BackColor = Color.FromArgb(28, 28, 32);
        split.SplitterDistance = 290;

        // Left
        var leftHeader = new Label
        {
            Text = "Maps (workspace\\ripped\\Content\\maps)",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.FromArgb(190, 190, 200),
            Padding = new Padding(6, 4, 0, 0),
        };
        split.Panel1.Controls.Add(leftHeader);

        _filterBox.Dock = DockStyle.Top;
        _filterBox.BackColor = Color.FromArgb(40, 40, 46);
        _filterBox.ForeColor = Color.White;
        _filterBox.BorderStyle = BorderStyle.FixedSingle;
        _filterBox.PlaceholderText = "filter...";
        split.Panel1.Controls.Add(_filterBox);
        _filterBox.BringToFront();

        _mapTree.Dock = DockStyle.Fill;
        _mapTree.BackColor = Color.FromArgb(24, 24, 28);
        _mapTree.ForeColor = Color.FromArgb(220, 220, 230);
        _mapTree.BorderStyle = BorderStyle.None;
        _mapTree.HideSelection = false;
        _mapTree.ShowLines = false;
        _mapTree.ShowRootLines = false;
        split.Panel1.Controls.Add(_mapTree);
        _mapTree.BringToFront();

        // Right
        _previewBox.Location = new Point(12, 8);
        _previewBox.Size = new Size(380, 280);
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _previewBox.BackColor = Color.FromArgb(18, 18, 22);
        _previewBox.BorderStyle = BorderStyle.FixedSingle;
        _previewBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        split.Panel2.Controls.Add(_previewBox);

        _previewMeta.Location = new Point(12, 292);
        _previewMeta.Size = new Size(380, 18);
        _previewMeta.ForeColor = Color.FromArgb(160, 160, 170);
        split.Panel2.Controls.Add(_previewMeta);

        split.Panel2.Controls.Add(new Label
        {
            Text = "Selected TMX:",
            Location = new Point(410, 8),
            Size = new Size(100, 18),
            ForeColor = Color.FromArgb(180, 180, 195),
        });
        _tmxBox.Location = new Point(410, 28);
        _tmxBox.Size = new Size(310, 22);
        _tmxBox.BackColor = Color.FromArgb(40, 40, 46);
        _tmxBox.ForeColor = Color.White;
        _tmxBox.BorderStyle = BorderStyle.FixedSingle;
        _tmxBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        split.Panel2.Controls.Add(_tmxBox);

        StyleButton(_btnBrowseTmx, new Point(724, 28), new Size(28, 22), Color.FromArgb(50, 50, 58));
        _btnBrowseTmx.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        split.Panel2.Controls.Add(_btnBrowseTmx);

        StyleButton(_btnTiled,    new Point(410, 64), new Size(140, 32), Color.FromArgb(90, 130, 70));
        StyleButton(_btnValidate, new Point(560, 64), new Size(120, 32), Color.FromArgb(70, 70, 80));
        split.Panel2.Controls.Add(_btnTiled);
        split.Panel2.Controls.Add(_btnValidate);

        StyleButton(_btnInstallTiled, new Point(410, 104), new Size(140, 26), Color.FromArgb(50, 50, 58));
        StyleButton(_btnChangeGame,   new Point(560, 104), new Size(140, 26), Color.FromArgb(50, 50, 58));
        split.Panel2.Controls.Add(_btnInstallTiled);
        split.Panel2.Controls.Add(_btnChangeGame);
    }

    private static Label NewPill() => new()
    {
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.FromArgb(90, 90, 100),
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        BorderStyle = BorderStyle.FixedSingle,
    };

    private static void SetPill(Label pill, int x, int y, int w)
    {
        pill.Location = new Point(x, y);
        pill.Size = new Size(w, 22);
    }

    private static void StyleButton(Button b, Point loc, Size size, Color back)
    {
        b.Location = loc;
        b.Size = size;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = back;
        b.ForeColor = Color.White;
    }

    // ---------- Events ----------

    private void WireEvents()
    {
        _btnRefresh.Click       += async (_, _) => await RefreshStatusAndTreeAsync();
        _btnOpenWorkspace.Click += (_, _) => OpenWorkspaceFolder();
        _btnRip.Click           += async (_, _) => await RipAsync();
        _btnPrepare.Click       += async (_, _) => await PrepareAsync();
        _btnChangeGame.Click    += (_, _) => ChangeGameFolder();
        _filterBox.TextChanged  += (_, _) => PopulateMapTree();
        _mapTree.AfterSelect    += (_, e) => OnMapSelected(e.Node);
        _btnBrowseTmx.Click     += (_, _) => BrowseTmx();
        _btnTiled.Click         += async (_, _) => await OpenInTiledAsync();
        _btnValidate.Click      += (_, _) => ValidateSelected();
        _btnInstallTiled.Click  += (_, _) => OpenTiledLocation();
    }

    private void OpenWorkspaceFolder()
    {
        if (!Directory.Exists(Paths.RippedRoot))
            Directory.CreateDirectory(Paths.RippedRoot);
        Process.Start(new ProcessStartInfo("explorer.exe", Paths.RippedRoot) { UseShellExecute = true });
    }

    private void ChangeGameFolder()
    {
        var prev = _config.GameRoot;
        _config.GameRoot = null;
        var picked = GameFolderResolver.Resolve(_config);
        if (string.IsNullOrEmpty(picked))
        {
            // User cancelled - restore previous.
            _config.GameRoot = prev;
            _config.Save();
            return;
        }
        Paths.SetGameRoot(picked);
        Text = $"Romestead Map Workshop  -  {picked}";
        Log($"Game folder changed to: {picked}");
        _ = RefreshStatusAndTreeAsync();
    }

    private async Task RipAsync()
    {
        var profile = Enum.Parse<RipProfile>((string)_profileCombo.SelectedItem!);
        await RunOpAsync($"Rip ({profile})", sink => Ripper.RipAsync(profile, force: true, sink));
        await RefreshStatusAndTreeAsync();
    }

    private async Task PrepareAsync()
    {
        await RunOpAsync("Prepare", async sink => { await Operations.EnsurePreparedAsync(sink); });
        await RefreshStatusAndTreeAsync();
    }

    private async Task OpenInTiledAsync()
    {
        var path = GetSelectedTmx();
        if (string.IsNullOrEmpty(path)) { ShowInfo("Select a map first."); return; }
        await RunOpAsync("Open in Tiled", sink => Operations.OpenInTiledAsync(path, sink));
        await RefreshStatusAndTreeAsync();
    }

    private void ValidateSelected()
    {
        var path = GetSelectedTmx();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Log("Select a TMX first."); return; }
        try
        {
            var r = TmxValidator.Validate(path);
            Log($"=== {r.Path} ===");
            foreach (var w in r.Warnings) Log("  WARN: " + w);
            foreach (var e in r.Errors)   Log("  ERROR: " + e);
            if (r.Errors.Count == 0 && r.Warnings.Count == 0) Log("  OK");
        }
        catch (Exception ex) { Log("validate error: " + ex.Message); }
    }

    private void OpenTiledLocation()
    {
        var tiled = Paths.FindTiledExe();
        if (!string.IsNullOrEmpty(tiled))
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + tiled + "\"") { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("https://www.mapeditor.org/") { UseShellExecute = true });
    }

    private void BrowseTmx()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Tiled map (*.tmx)|*.tmx",
            InitialDirectory = Directory.Exists(Paths.RippedRoot) ? Paths.RippedRoot : Paths.GameRoot,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _tmxBox.Text = dlg.FileName;
            ShowPreview(dlg.FileName);
        }
    }

    private void OnMapSelected(TreeNode? node)
    {
        if (node?.Tag is not string path) return;
        _tmxBox.Text = path;
        ShowPreview(path);
    }

    // ---------- Status / Tree / Preview ----------

    private async Task RefreshStatusAndTreeAsync()
    {
        var status = await Task.Run(WorkspaceStatus.Probe);
        ApplyStatus(status);
        PopulateMapTree();
    }

    private void ApplyStatus(WorkspaceStatus s)
    {
        var green  = Color.FromArgb(80, 180, 90);
        var yellow = Color.FromArgb(210, 170, 60);
        var red    = Color.FromArgb(200, 70, 70);
        var grey   = Color.FromArgb(90, 90, 100);

        _lightRip.BackColor = s.Ripped ? green : red;
        _lightRip.Text = s.Ripped ? "Ripped" : "Not ripped";

        if (!s.Ripped)              { _lightXnb.BackColor = grey;   _lightXnb.Text = "XNB: -"; }
        else if (s.XnbPending == 0) { _lightXnb.BackColor = green;  _lightXnb.Text = "XNB done"; }
        else                        { _lightXnb.BackColor = yellow; _lightXnb.Text = $"XNB pending: {s.XnbPending}"; }

        if (!s.Ripped)              { _lightTsx.BackColor = grey;   _lightTsx.Text = "Paths: -"; }
        else if (s.TsxBroken == 0)  { _lightTsx.BackColor = green;  _lightTsx.Text = "Paths OK"; }
        else                        { _lightTsx.BackColor = yellow; _lightTsx.Text = $"{s.TsxBroken} tsx broken"; }

        _lightTiled.BackColor = string.IsNullOrEmpty(s.TiledExe) ? red : green;
        _lightTiled.Text = string.IsNullOrEmpty(s.TiledExe) ? "Tiled missing" : "Tiled installed";
    }

    private void PopulateMapTree()
    {
        _mapTree.BeginUpdate();
        _mapTree.Nodes.Clear();
        if (!Directory.Exists(Paths.RippedMaps))
        {
            var n = _mapTree.Nodes.Add("(rip game Content to populate)");
            n.ForeColor = Color.Gray;
            _mapTree.EndUpdate();
            return;
        }

        var filter = _filterBox.Text.Trim().ToLowerInvariant();
        var byDir = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(Paths.RippedMaps, "*.tmx", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Paths.RippedMaps, f);
            if (filter.Length > 0 && rel.ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0)
                continue;
            var dir = Path.GetDirectoryName(rel);
            if (string.IsNullOrEmpty(dir)) dir = "(root)";
            if (!byDir.TryGetValue(dir, out var list)) byDir[dir] = list = new List<string>();
            list.Add(f);
        }

        foreach (var kv in byDir)
        {
            var dirNode = _mapTree.Nodes.Add(kv.Key);
            dirNode.ForeColor = Color.FromArgb(180, 180, 195);
            foreach (var f in kv.Value.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var leaf = dirNode.Nodes.Add(Path.GetFileNameWithoutExtension(f));
                leaf.Tag = f;
                leaf.ForeColor = Color.FromArgb(220, 220, 230);
            }
            if (filter.Length > 0) dirNode.ExpandAll();
            else if (kv.Key.Contains("interiors_new", StringComparison.OrdinalIgnoreCase)
                  || kv.Key.Contains("buildings", StringComparison.OrdinalIgnoreCase)
                  || kv.Key.Contains("dungeons", StringComparison.OrdinalIgnoreCase))
                dirNode.Expand();
        }
        _mapTree.EndUpdate();
    }

    private void ShowPreview(string tmxPath)
    {
        try { ShowPreviewCore(tmxPath); }
        catch (Exception ex)
        {
            Log($"preview error: {ex.GetType().Name}: {ex.Message}");
            try { _previewBox.Image?.Dispose(); _previewBox.Image = null; } catch { }
            _previewMeta.Text = "(preview failed - see log)";
        }
    }

    private void ShowPreviewCore(string tmxPath)
    {
        if (_previewBox.Image != null)
        {
            var old = _previewBox.Image;
            _previewBox.Image = null;
            old.Dispose();
        }
        _previewMeta.Text = "";

        var info = TmxInfoReader.Read(tmxPath);
        if (info == null) return;

        var meta = info.Width.HasValue
            ? $"{info.Width} x {info.Height} tiles, tile {info.TileW}x{info.TileH} px"
            : "";

        var rendered = TmxRenderer.Render(tmxPath, warn: w => Log($"[preview] {w}"));
        if (rendered != null)
        {
            _previewBox.Image = rendered;
            meta += (meta.Length > 0 ? "  |  " : "") + "rendered";
            _previewMeta.Text = meta;
            return;
        }

        if (!string.IsNullOrEmpty(info.FirstImagePathAbsolute))
        {
            try
            {
                using var fs = File.Open(info.FirstImagePathAbsolute, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var temp = Image.FromStream(fs);
                _previewBox.Image = new Bitmap(temp);
                meta += (meta.Length > 0 ? "  |  " : "") + Path.GetFileName(info.FirstImagePathAbsolute);
            }
            catch (Exception ex)
            {
                meta += $"  |  (image load failed: {ex.Message})";
            }
        }
        else
        {
            meta += "  |  no background image (tile-only or missing)";
        }
        _previewMeta.Text = meta;
    }

    // ---------- Operation runner ----------

    private async Task RunOpAsync(string label, Func<IProgressSink, Task> op)
    {
        if (_runningCts != null) { Log("(busy)"); return; }
        SetBusy(true, label);
        _runningCts = new CancellationTokenSource();
        try
        {
            var sink = new UiSink(this, _runningCts.Token);
            await op(sink);
        }
        catch (OperationCanceledException) { Log("(cancelled)"); }
        catch (Exception ex) { Log($"ERROR: {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            _runningCts.Dispose();
            _runningCts = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string op = "")
    {
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        _progress.MarqueeAnimationSpeed = busy ? 30 : 0;
        _statusLabel.Text = busy ? $"Running: {op}" : "Ready";
        foreach (var b in _actionButtons) b.Enabled = !busy;
    }

    private string? GetSelectedTmx()
    {
        if (_mapTree.SelectedNode?.Tag is string s) return s;
        var manual = _tmxBox.Text.Trim();
        return string.IsNullOrEmpty(manual) ? null : manual;
    }

    private void ShowInfo(string msg) => MessageBox.Show(this, msg, "Map Workshop");

    public void Log(string line)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => Log(line))); return; }
        _logBox.AppendText(line + Environment.NewLine);
        if (_logBox.TextLength > 200_000)
        {
            var keep = _logBox.Text.Substring(_logBox.TextLength - 100_000);
            _logBox.Text = "[log trimmed]\r\n" + keep;
        }
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    public void SetStatus(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text))); return; }
        _statusLabel.Text = text;
    }

    private sealed class UiSink : IProgressSink
    {
        private readonly MainForm _form;
        public UiSink(MainForm form, CancellationToken ct) { _form = form; CancellationToken = ct; }
        public void Log(string line) => _form.Log(line);
        public void Status(string text) => _form.SetStatus("Running: " + text);
        public CancellationToken CancellationToken { get; }
    }
}
