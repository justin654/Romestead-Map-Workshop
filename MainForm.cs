using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private const string GitHubUrl = "https://github.com/";
    private const string TiledUrl = "https://www.mapeditor.org/";

    private readonly AppConfig _config;
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };

    // ---- Menu bar ----
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _miChangeGame   = new("Change &game folder...");
    private readonly ToolStripMenuItem _miOpenWorkspace = new("Open &workspace folder");
    private readonly ToolStripMenuItem _miExit         = new("E&xit");
    private readonly ToolStripMenuItem _miRipMap       = new("Rip Content - &MapAuthor (recommended)");
    private readonly ToolStripMenuItem _miRipInt       = new("Rip Content - &Interiors only");
    private readonly ToolStripMenuItem _miRipFull      = new("Rip Content - &Full (~1.5 GB)");
    private readonly ToolStripMenuItem _miRefresh      = new("&Refresh status") { ShortcutKeys = Keys.F5 };
    private readonly ToolStripMenuItem _miWhereIsTiled = new("Where is &Tiled?");
    private readonly ToolStripMenuItem _miGitHub       = new("Open &GitHub page");
    private readonly ToolStripMenuItem _miAbout        = new("&About...");

    // ---- Top status strip ----
    private readonly Panel _statusStrip = new();
    private readonly Label _statusGame = new();
    private readonly Label _statusRipped = new();
    private readonly Label _statusXnb = new();
    private readonly Label _statusTsx = new();
    private readonly Label _statusTiled = new();

    // ---- Center: Welcome (empty state) ----
    private readonly Panel _welcome = new();
    private readonly ComboBox _welcomeProfile = new();
    private readonly Button _welcomeRipBtn = new() { Text = "Rip game Content" };

    // ---- Center: Workspace (split) ----
    private readonly SplitContainer _split = new();
    private readonly TextBox _filterBox = new();
    private readonly TreeView _mapTree = new();
    private readonly ImageList _treeIcons = new() { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

    private readonly Panel _rightPanel = new();
    private readonly Panel _rightHeader = new();
    private readonly Label _selectedMapLabel = new();
    private readonly Label _selectedMapMeta  = new();
    private readonly PictureBox _previewBox = new();
    private readonly Panel _rightActions = new();
    private readonly Button _btnTiled    = new() { Text = "Edit in Tiled" };
    private readonly Button _btnValidate = new() { Text = "Validate paths" };

    // ---- Bottom: collapsible log + progress + status ----
    private readonly Panel _logContainer = new();
    private readonly Panel _logHeader = new();
    private readonly Label _logHeaderLabel = new() { Text = "▲  Log" };
    private readonly Label _logCopyLink   = new() { Text = "Copy", Cursor = Cursors.Hand };
    private readonly TextBox _logBox = new();
    private readonly ProgressBar _progress = new();
    private readonly Panel _bottomStatus = new();
    private readonly Label _bottomStatusLabel = new() { Text = "Ready" };

    private const int LogExpandedHeight  = 200;
    private const int LogCollapsedHeight = 0;
    private bool _logExpanded = false;

    private CancellationTokenSource? _runningCts;
    private readonly List<Component> _busyTargets;  // disabled during long ops

    public MainForm(AppConfig config)
    {
        _config = config;

        Text = "Romestead Map Workshop";
        ClientSize = new Size(1120, 740);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(28, 28, 32);
        ForeColor = Color.FromArgb(232, 230, 227);
        Font = new Font("Segoe UI", 9f);

        _busyTargets = new List<Component>
        {
            _miChangeGame, _miOpenWorkspace, _miRipMap, _miRipInt, _miRipFull,
            _miRefresh, _miWhereIsTiled,
            _welcomeRipBtn, _btnTiled, _btnValidate,
        };

        BuildTreeIcons();
        BuildMenu();
        BuildStatusStrip();
        BuildBottom();
        BuildWelcome();
        BuildWorkspace();
        WireEvents();
        WireTooltips();

        _ = RefreshStatusAndTreeAsync();
    }

    // -------------------- Layout --------------------

    private void BuildMenu()
    {
        _menu.BackColor = Color.FromArgb(28, 28, 32);
        _menu.ForeColor = Color.FromArgb(230, 230, 235);
        _menu.Renderer = new DarkMenuRenderer();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            _miChangeGame,
            _miOpenWorkspace,
            new ToolStripSeparator(),
            _miExit,
        });

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.AddRange(new ToolStripItem[]
        {
            _miRipMap,
            _miRipInt,
            _miRipFull,
            new ToolStripSeparator(),
            _miRefresh,
            new ToolStripSeparator(),
            _miWhereIsTiled,
        });

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.AddRange(new ToolStripItem[]
        {
            _miGitHub,
            _miAbout,
        });

        _menu.Items.AddRange(new ToolStripItem[] { file, tools, help });
        MainMenuStrip = _menu;
        Controls.Add(_menu);
    }

    private void BuildStatusStrip()
    {
        _statusStrip.Dock = DockStyle.Top;
        _statusStrip.Height = 26;
        _statusStrip.BackColor = Color.FromArgb(22, 22, 26);
        _statusStrip.Padding = new Padding(10, 4, 10, 4);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
        };

        foreach (var lbl in new[] { _statusGame, _statusRipped, _statusXnb, _statusTsx, _statusTiled })
        {
            lbl.AutoSize = true;
            lbl.Padding = new Padding(0, 2, 18, 0);
            lbl.ForeColor = Color.FromArgb(190, 190, 200);
            flow.Controls.Add(lbl);
        }

        _statusStrip.Controls.Add(flow);
        Controls.Add(_statusStrip);
    }

    private void BuildBottom()
    {
        // Order matters: deepest-bottom docks first.
        _bottomStatus.Dock = DockStyle.Bottom;
        _bottomStatus.Height = 22;
        _bottomStatus.BackColor = Color.FromArgb(18, 18, 22);

        _bottomStatusLabel.Dock = DockStyle.Fill;
        _bottomStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _bottomStatusLabel.Padding = new Padding(10, 0, 10, 0);
        _bottomStatusLabel.ForeColor = Color.FromArgb(150, 200, 150);

        _progress.Dock = DockStyle.Right;
        _progress.Width = 220;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Visible = false;

        _bottomStatus.Controls.Add(_bottomStatusLabel);
        _bottomStatus.Controls.Add(_progress);
        Controls.Add(_bottomStatus);

        // Log container - collapsible.
        _logContainer.Dock = DockStyle.Bottom;
        _logContainer.Height = LogCollapsedHeight + 24;   // header only at start
        _logContainer.BackColor = Color.FromArgb(22, 22, 26);

        _logHeader.Dock = DockStyle.Top;
        _logHeader.Height = 24;
        _logHeader.BackColor = Color.FromArgb(34, 34, 40);
        _logHeader.Cursor = Cursors.Hand;

        _logHeaderLabel.Dock = DockStyle.Left;
        _logHeaderLabel.AutoSize = false;
        _logHeaderLabel.Width = 100;
        _logHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        _logHeaderLabel.Padding = new Padding(10, 0, 0, 0);
        _logHeaderLabel.ForeColor = Color.FromArgb(200, 200, 215);
        _logHeaderLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _logHeaderLabel.Cursor = Cursors.Hand;

        _logCopyLink.Dock = DockStyle.Right;
        _logCopyLink.AutoSize = false;
        _logCopyLink.Width = 60;
        _logCopyLink.TextAlign = ContentAlignment.MiddleCenter;
        _logCopyLink.ForeColor = Color.FromArgb(140, 170, 230);

        _logHeader.Controls.Add(_logHeaderLabel);
        _logHeader.Controls.Add(_logCopyLink);
        _logContainer.Controls.Add(_logHeader);

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
        _logBox.Visible = false;
        _logContainer.Controls.Add(_logBox);
        _logBox.BringToFront();
        _logHeader.BringToFront();

        Controls.Add(_logContainer);
    }

    private void BuildWelcome()
    {
        _welcome.Dock = DockStyle.Fill;
        _welcome.BackColor = Color.FromArgb(28, 28, 32);
        _welcome.Visible = false;

        var inner = new Panel
        {
            Anchor = AnchorStyles.None,
            Size = new Size(520, 260),
            BackColor = Color.Transparent,
        };

        var title = new Label
        {
            Text = "Welcome to Map Workshop",
            Font = new Font("Segoe UI Light", 22f),
            ForeColor = Color.FromArgb(240, 240, 245),
            Location = new Point(0, 0),
            Size = new Size(520, 40),
            TextAlign = ContentAlignment.MiddleCenter,
        };

        var body = new Label
        {
            Text = "Pick a profile and click Rip to copy Romestead's Content folder " +
                   "into a workspace where you can edit maps in Tiled.",
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(190, 190, 200),
            Location = new Point(0, 50),
            Size = new Size(520, 60),
            TextAlign = ContentAlignment.MiddleCenter,
        };

        var profileLbl = new Label
        {
            Text = "Profile:",
            Location = new Point(150, 130),
            Size = new Size(60, 22),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(190, 190, 200),
        };

        _welcomeProfile.DropDownStyle = ComboBoxStyle.DropDownList;
        _welcomeProfile.Items.AddRange(new object[] { "MapAuthor", "Interiors", "Full" });
        _welcomeProfile.SelectedIndex = 0;
        _welcomeProfile.Location = new Point(216, 128);
        _welcomeProfile.Size = new Size(154, 24);
        _welcomeProfile.BackColor = Color.FromArgb(40, 40, 46);
        _welcomeProfile.ForeColor = Color.White;
        _welcomeProfile.FlatStyle = FlatStyle.Flat;

        _welcomeRipBtn.Location = new Point(160, 170);
        _welcomeRipBtn.Size = new Size(200, 40);
        _welcomeRipBtn.FlatStyle = FlatStyle.Flat;
        _welcomeRipBtn.BackColor = Color.FromArgb(60, 100, 160);
        _welcomeRipBtn.ForeColor = Color.White;
        _welcomeRipBtn.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);

        var hint = new Label
        {
            Text = "MapAuthor copies only maps + tilesets + media (~200 MB). Full copies the entire game Content (~1.5 GB).",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(130, 130, 140),
            Location = new Point(0, 220),
            Size = new Size(520, 36),
            TextAlign = ContentAlignment.MiddleCenter,
        };

        inner.Controls.AddRange(new Control[] { title, body, profileLbl, _welcomeProfile, _welcomeRipBtn, hint });
        _welcome.Controls.Add(inner);
        _welcome.Resize += (_, _) =>
        {
            inner.Left = (_welcome.ClientSize.Width - inner.Width) / 2;
            inner.Top  = Math.Max(40, (_welcome.ClientSize.Height - inner.Height) / 2);
        };

        Controls.Add(_welcome);
    }

    private void BuildWorkspace()
    {
        _split.Dock = DockStyle.Fill;
        _split.BackColor = Color.FromArgb(28, 28, 32);
        _split.FixedPanel = FixedPanel.Panel1;
        Controls.Add(_split);
        _split.BringToFront();
        _split.Panel1.BackColor = Color.FromArgb(24, 24, 28);
        _split.Panel2.BackColor = Color.FromArgb(28, 28, 32);
        _split.SplitterDistance = 290;

        // --- Left (map tree + filter)
        var leftHeader = new Label
        {
            Text = "Maps",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Color.FromArgb(190, 190, 200),
            Padding = new Padding(10, 4, 0, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        };
        _split.Panel1.Controls.Add(leftHeader);

        _filterBox.Dock = DockStyle.Top;
        _filterBox.BackColor = Color.FromArgb(40, 40, 46);
        _filterBox.ForeColor = Color.White;
        _filterBox.BorderStyle = BorderStyle.FixedSingle;
        _filterBox.PlaceholderText = "Filter maps...";
        _split.Panel1.Controls.Add(_filterBox);
        _filterBox.BringToFront();

        _mapTree.Dock = DockStyle.Fill;
        _mapTree.BackColor = Color.FromArgb(24, 24, 28);
        _mapTree.ForeColor = Color.FromArgb(220, 220, 230);
        _mapTree.BorderStyle = BorderStyle.None;
        _mapTree.HideSelection = false;
        _mapTree.ShowLines = false;
        _mapTree.ShowRootLines = false;
        _mapTree.ImageList = _treeIcons;
        _mapTree.FullRowSelect = true;
        _mapTree.ItemHeight = 22;
        _split.Panel1.Controls.Add(_mapTree);
        _mapTree.BringToFront();

        // --- Right (preview + actions)
        _rightPanel.Dock = DockStyle.Fill;
        _rightPanel.BackColor = Color.FromArgb(28, 28, 32);
        _split.Panel2.Controls.Add(_rightPanel);

        _rightHeader.Dock = DockStyle.Top;
        _rightHeader.Height = 44;
        _rightHeader.BackColor = Color.FromArgb(28, 28, 32);
        _rightHeader.Padding = new Padding(14, 8, 14, 0);

        _selectedMapLabel.Dock = DockStyle.Top;
        _selectedMapLabel.AutoSize = false;
        _selectedMapLabel.Height = 22;
        _selectedMapLabel.Text = "No map selected";
        _selectedMapLabel.ForeColor = Color.FromArgb(220, 220, 235);
        _selectedMapLabel.Font = new Font("Segoe UI", 11f, FontStyle.Bold);

        _selectedMapMeta.Dock = DockStyle.Top;
        _selectedMapMeta.AutoSize = false;
        _selectedMapMeta.Height = 14;
        _selectedMapMeta.Text = "";
        _selectedMapMeta.ForeColor = Color.FromArgb(150, 150, 165);
        _selectedMapMeta.Font = new Font("Segoe UI", 8.5f);

        _rightHeader.Controls.Add(_selectedMapMeta);
        _rightHeader.Controls.Add(_selectedMapLabel);
        _rightPanel.Controls.Add(_rightHeader);

        _rightActions.Dock = DockStyle.Bottom;
        _rightActions.Height = 56;
        _rightActions.BackColor = Color.FromArgb(28, 28, 32);
        _rightActions.Padding = new Padding(14, 10, 14, 12);

        _btnTiled.Dock = DockStyle.Left;
        _btnTiled.Width = 170;
        _btnTiled.FlatStyle = FlatStyle.Flat;
        _btnTiled.BackColor = Color.FromArgb(90, 130, 70);
        _btnTiled.ForeColor = Color.White;
        _btnTiled.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

        _btnValidate.Dock = DockStyle.Left;
        _btnValidate.Width = 130;
        _btnValidate.FlatStyle = FlatStyle.Flat;
        _btnValidate.BackColor = Color.FromArgb(70, 70, 80);
        _btnValidate.ForeColor = Color.White;
        _btnValidate.Margin = new Padding(8, 0, 0, 0);

        // Add in reverse order because Dock=Left stacks left-to-right by add order.
        _rightActions.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 8, BackColor = Color.Transparent });
        _rightActions.Controls.Add(_btnValidate);
        _rightActions.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 8, BackColor = Color.Transparent });
        _rightActions.Controls.Add(_btnTiled);
        _rightPanel.Controls.Add(_rightActions);

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _previewBox.BackColor = Color.FromArgb(18, 18, 22);
        _previewBox.Margin = new Padding(14, 6, 14, 6);
        // Wrap in a Panel so we can give it an inset border + padding.
        var previewWrap = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 28, 32),
            Padding = new Padding(14, 6, 14, 6),
        };
        var previewInner = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 22), BorderStyle = BorderStyle.FixedSingle };
        previewInner.Controls.Add(_previewBox);
        previewWrap.Controls.Add(previewInner);
        _rightPanel.Controls.Add(previewWrap);
        previewWrap.BringToFront();
    }

    private void BuildTreeIcons()
    {
        // Render Segoe MDL2 Assets glyphs into tiny bitmaps. Available on Win10+.
        _treeIcons.Images.Add("folder", GlyphIcon("", Color.FromArgb(220, 195, 110))); // folder
        _treeIcons.Images.Add("map",    GlyphIcon("", Color.FromArgb(150, 195, 230))); // map/world
    }

    private static Bitmap GlyphIcon(string glyph, Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe MDL2 Assets", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        var size = g.MeasureString(glyph, font);
        g.DrawString(glyph, font, brush,
            (16 - size.Width) / 2f,
            (16 - size.Height) / 2f);
        return bmp;
    }

    // -------------------- Tooltips --------------------

    private void WireTooltips()
    {
        _tips.SetToolTip(_filterBox, "Type to filter the map list. Searches the full relative path.");
        _tips.SetToolTip(_btnTiled, "Auto-converts XNB textures and repairs tileset paths if needed, then opens the map in Tiled.");
        _tips.SetToolTip(_btnValidate, "Check the selected TMX for path conventions Romestead expects.");
        _tips.SetToolTip(_welcomeRipBtn, "Copies the game's Content folder into your workspace so Tiled can read it.");
        _tips.SetToolTip(_welcomeProfile, "MapAuthor: maps + tilesets + media (smallest). Interiors: only interiors_new + building_exteriors. Full: entire Content tree (~1.5 GB).");
        _tips.SetToolTip(_logHeaderLabel, "Click to expand/collapse the log");
        _tips.SetToolTip(_logCopyLink, "Copy the full log to clipboard");
        _tips.SetToolTip(_statusGame, "Path to your Romestead game install. Use File > Change game folder to update.");
        _tips.SetToolTip(_statusRipped, "Whether the game's Content tree has been ripped to the workspace.");
        _tips.SetToolTip(_statusXnb, "Number of XNB textures still needing conversion to PNG.");
        _tips.SetToolTip(_statusTsx, "Number of .tsx tileset files whose image path doesn't resolve.");
        _tips.SetToolTip(_statusTiled, "Whether Tiled.exe was found on this machine.");
    }

    // -------------------- Wire-up --------------------

    private void WireEvents()
    {
        _miChangeGame.Click    += (_, _) => ChangeGameFolder();
        _miOpenWorkspace.Click += (_, _) => OpenWorkspaceFolder();
        _miExit.Click          += (_, _) => Close();

        _miRipMap.Click  += async (_, _) => await RipAsync(RipProfile.MapAuthor);
        _miRipInt.Click  += async (_, _) => await RipAsync(RipProfile.Interiors);
        _miRipFull.Click += async (_, _) => await RipAsync(RipProfile.Full);

        _miRefresh.Click      += async (_, _) => await RefreshStatusAndTreeAsync();
        _miWhereIsTiled.Click += (_, _) => OpenTiledLocation();
        _miGitHub.Click       += (_, _) => OpenUrl(GitHubUrl);
        _miAbout.Click        += (_, _) => ShowAbout();

        _welcomeRipBtn.Click += async (_, _) =>
        {
            var profile = Enum.Parse<RipProfile>((string)_welcomeProfile.SelectedItem!);
            await RipAsync(profile);
        };

        _filterBox.TextChanged += (_, _) => PopulateMapTree();
        _mapTree.AfterSelect   += (_, e) => OnMapSelected(e.Node);

        _btnTiled.Click    += async (_, _) => await OpenInTiledAsync();
        _btnValidate.Click += (_, _) => ValidateSelected();

        _logHeader.Click       += (_, _) => ToggleLog();
        _logHeaderLabel.Click  += (_, _) => ToggleLog();
        _logCopyLink.Click     += (_, _) => CopyLog();
    }

    // -------------------- Actions --------------------

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
            _config.GameRoot = prev;
            _config.Save();
            return;
        }
        Paths.SetGameRoot(picked);
        Log($"Game folder changed to: {picked}");
        _ = RefreshStatusAndTreeAsync();
    }

    private async Task RipAsync(RipProfile profile)
    {
        await RunOpAsync($"Rip ({profile})", sink => Ripper.RipAsync(profile, force: true, sink));
        await RefreshStatusAndTreeAsync();
    }

    private async Task OpenInTiledAsync()
    {
        var path = GetSelectedTmx();
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show(this, "Pick a map on the left first.", "Map Workshop");
            return;
        }
        await RunOpAsync("Open in Tiled", sink => Operations.OpenInTiledAsync(path, sink));
        await RefreshStatusAndTreeAsync();
    }

    private void ValidateSelected()
    {
        var path = GetSelectedTmx();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Log("Select a TMX first.");
            ExpandLog();
            return;
        }
        try
        {
            var r = TmxValidator.Validate(path);
            Log($"=== {r.Path} ===");
            foreach (var w in r.Warnings) Log("  WARN: " + w);
            foreach (var e in r.Errors)   Log("  ERROR: " + e);
            if (r.Errors.Count == 0 && r.Warnings.Count == 0) Log("  OK");
            ExpandLog();
        }
        catch (Exception ex) { Log("validate error: " + ex.Message); ExpandLog(); }
    }

    private void OpenTiledLocation()
    {
        var tiled = Paths.FindTiledExe();
        if (!string.IsNullOrEmpty(tiled))
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + tiled + "\"") { UseShellExecute = true });
        else
            OpenUrl(TiledUrl);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void ShowAbout()
    {
        var ver = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.0";
        MessageBox.Show(this,
            $"Romestead Map Workshop\nVersion {ver}\n\n" +
            "Community map-editing tool for Romestead.\n" +
            "MIT licensed. See LICENSE.\n\n" +
            $"Workspace: {Paths.Workspace}\n" +
            $"Game:      {Paths.GameRoot}",
            "About Map Workshop",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // -------------------- Map tree + selection --------------------

    private string? GetSelectedTmx()
    {
        if (_mapTree.SelectedNode?.Tag is string s) return s;
        return null;
    }

    private void OnMapSelected(TreeNode? node)
    {
        if (node?.Tag is not string path) return;
        _selectedMapLabel.Text = Path.GetFileNameWithoutExtension(path);
        var rel = Path.GetRelativePath(Paths.RippedMaps, path).Replace('\\', '/');
        _selectedMapMeta.Text = "maps/" + rel.Replace('\\', '/');
        ShowPreview(path);
    }

    private async Task RefreshStatusAndTreeAsync()
    {
        var status = await Task.Run(WorkspaceStatus.Probe);
        ApplyStatus(status);
        SetEmptyState(!status.Ripped);
        if (status.Ripped) PopulateMapTree();
    }

    private void ApplyStatus(WorkspaceStatus s)
    {
        var ok   = Color.FromArgb(120, 200, 130);
        var warn = Color.FromArgb(220, 190, 90);
        var bad  = Color.FromArgb(220, 110, 110);
        var muted = Color.FromArgb(150, 150, 165);

        _statusGame.Text     = "Game: " + Truncate(Paths.GameRoot, 60);
        _statusGame.ForeColor = muted;

        _statusRipped.Text = s.Ripped ? "Ripped" : "Not ripped";
        _statusRipped.ForeColor = s.Ripped ? ok : bad;

        if (!s.Ripped) { _statusXnb.Text = "XNB: n/a"; _statusXnb.ForeColor = muted; }
        else if (s.XnbPending == 0) { _statusXnb.Text = "XNB ready"; _statusXnb.ForeColor = ok; }
        else { _statusXnb.Text = $"XNB pending: {s.XnbPending}"; _statusXnb.ForeColor = warn; }

        if (!s.Ripped) { _statusTsx.Text = "Paths: n/a"; _statusTsx.ForeColor = muted; }
        else if (s.TsxBroken == 0) { _statusTsx.Text = "Paths OK"; _statusTsx.ForeColor = ok; }
        else { _statusTsx.Text = $"{s.TsxBroken} tsx broken"; _statusTsx.ForeColor = warn; }

        if (string.IsNullOrEmpty(s.TiledExe)) { _statusTiled.Text = "Tiled not installed"; _statusTiled.ForeColor = bad; }
        else                                   { _statusTiled.Text = "Tiled installed";    _statusTiled.ForeColor = ok; }
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        return s.Substring(0, 3) + "..." + s.Substring(s.Length - (max - 6));
    }

    private void SetEmptyState(bool empty)
    {
        _welcome.Visible = empty;
        _split.Visible = !empty;
        if (empty) _welcome.BringToFront();
        else        _split.BringToFront();
    }

    private void PopulateMapTree()
    {
        _mapTree.BeginUpdate();
        var expanded = new HashSet<string>(_mapTree.Nodes
            .OfType<TreeNode>()
            .Where(n => n.IsExpanded)
            .Select(n => n.Text), StringComparer.OrdinalIgnoreCase);
        _mapTree.Nodes.Clear();

        if (!Directory.Exists(Paths.RippedMaps))
        {
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
            dirNode.ForeColor = Color.FromArgb(200, 200, 215);
            dirNode.ImageIndex = 0;
            dirNode.SelectedImageIndex = 0;
            foreach (var f in kv.Value.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var leaf = dirNode.Nodes.Add(Path.GetFileNameWithoutExtension(f));
                leaf.Tag = f;
                leaf.ForeColor = Color.FromArgb(220, 220, 230);
                leaf.ImageIndex = 1;
                leaf.SelectedImageIndex = 1;
            }
            if (filter.Length > 0) dirNode.ExpandAll();
            else if (expanded.Contains(kv.Key)
                  || kv.Key.Contains("interiors_new", StringComparison.OrdinalIgnoreCase)
                  || kv.Key.Contains("buildings", StringComparison.OrdinalIgnoreCase)
                  || kv.Key.Contains("dungeons", StringComparison.OrdinalIgnoreCase))
                dirNode.Expand();
        }
        _mapTree.EndUpdate();
    }

    // -------------------- Preview --------------------

    private void ShowPreview(string tmxPath)
    {
        try { ShowPreviewCore(tmxPath); }
        catch (Exception ex)
        {
            Log($"preview error: {ex.GetType().Name}: {ex.Message}");
            try { _previewBox.Image?.Dispose(); _previewBox.Image = null; } catch { }
            _selectedMapMeta.Text = "(preview failed - see log)";
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

        var info = TmxInfoReader.Read(tmxPath);
        if (info == null) return;

        var rel = Path.GetRelativePath(Paths.RippedMaps, tmxPath).Replace('\\', '/');
        var dims = info.Width.HasValue ? $"  ·  {info.Width}×{info.Height} tiles · {info.TileW}×{info.TileH} px" : "";
        _selectedMapMeta.Text = "maps/" + rel + dims;

        var rendered = TmxRenderer.Render(tmxPath, warn: w => Log($"[preview] {w}"));
        if (rendered != null) { _previewBox.Image = rendered; return; }

        if (!string.IsNullOrEmpty(info.FirstImagePathAbsolute))
        {
            try
            {
                using var fs = File.Open(info.FirstImagePathAbsolute, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var temp = Image.FromStream(fs);
                _previewBox.Image = new Bitmap(temp);
            }
            catch (Exception ex) { Log("preview load failed: " + ex.Message); }
        }
    }

    // -------------------- Log panel --------------------

    private void ToggleLog() => SetLogExpanded(!_logExpanded);
    private void ExpandLog()   { if (!_logExpanded) SetLogExpanded(true); }

    private void SetLogExpanded(bool expanded)
    {
        _logExpanded = expanded;
        _logBox.Visible = expanded;
        _logContainer.Height = expanded ? LogExpandedHeight + 24 : 24;
        _logHeaderLabel.Text = expanded ? "▼  Log" : "▲  Log";
    }

    private void CopyLog()
    {
        try
        {
            if (_logBox.TextLength > 0)
                Clipboard.SetText(_logBox.Text);
        }
        catch { }
    }

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
        _bottomStatusLabel.Text = text;
    }

    // -------------------- Op runner --------------------

    private async Task RunOpAsync(string label, Func<IProgressSink, Task> op)
    {
        if (_runningCts != null) { Log("(busy)"); return; }
        SetBusy(true, label);
        ExpandLog();   // user wants to see what's happening during an op
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
        _progress.Visible = busy;
        _progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        _progress.MarqueeAnimationSpeed = busy ? 30 : 0;
        _bottomStatusLabel.Text = busy ? $"Running: {op}" : "Ready";
        foreach (var t in _busyTargets)
        {
            switch (t)
            {
                case Control c: c.Enabled = !busy; break;
                case ToolStripItem i: i.Enabled = !busy; break;
            }
        }
    }

    private sealed class UiSink : IProgressSink
    {
        private readonly MainForm _form;
        public UiSink(MainForm form, CancellationToken ct) { _form = form; CancellationToken = ct; }
        public void Log(string line) => _form.Log(line);
        public void Status(string text) => _form.SetStatus("Running: " + text);
        public CancellationToken CancellationToken { get; }
    }

    // -------------------- Dark menu rendering --------------------

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? (e.Item.Selected ? Color.White : Color.FromArgb(230, 230, 235))
                : Color.FromArgb(120, 120, 130);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(210, 210, 220);
            base.OnRenderArrow(e);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected            => Color.FromArgb(60, 90, 140);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 90, 140);
        public override Color MenuItemSelectedGradientEnd   => Color.FromArgb(60, 90, 140);
        public override Color MenuItemBorder              => Color.FromArgb(60, 90, 140);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(36, 36, 42);
        public override Color MenuItemPressedGradientEnd   => Color.FromArgb(36, 36, 42);
        public override Color ToolStripDropDownBackground => Color.FromArgb(36, 36, 42);
        public override Color ImageMarginGradientBegin    => Color.FromArgb(36, 36, 42);
        public override Color ImageMarginGradientMiddle   => Color.FromArgb(36, 36, 42);
        public override Color ImageMarginGradientEnd      => Color.FromArgb(36, 36, 42);
        public override Color MenuBorder                  => Color.FromArgb(60, 60, 70);
        public override Color SeparatorDark               => Color.FromArgb(60, 60, 70);
        public override Color SeparatorLight              => Color.FromArgb(40, 40, 46);
    }
}