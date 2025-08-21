using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CmdKit.Models;
using CmdKit.Settings;
using CmdKit.Theme;

namespace CmdKit;

public partial class CmdKitForm : Sunny.UI.UIForm
{
    private readonly Sunny.UI.UIStyleManager _styleMgr = new();
    private readonly string _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CmdKit");
    private string _dataFile = string.Empty;
    private List<CommandEntry> _all = new();
    private readonly ToolTip _listTooltip = new();
    private DateTime _lastToolTipTime = DateTime.MinValue;
    private AppSettings _settings = AppSettings.Load();
    private NotifyIcon? _trayIcon;
    private bool _allowExit = false;
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_CONTROL = 0x0002;
    private const int HOTKEY_ID = 0x1100; // arbitrary id
    private CancellationTokenSource? _filterCts;
    internal readonly HashSet<string> _kinds = new(StringComparer.OrdinalIgnoreCase) { "Command", "Link", "URL", "Password" };
    private readonly System.Windows.Forms.Timer _tooltipTimer = new();
    private int _hoverIndex = -1;
    private readonly Font _uiFont = CreateUiFont();
    private Font? _tooltipFontCjk; // fallback for CJK
    private bool _themeReappliedAfterShown = false; // ensure dark theme reapplied once after shown
    private bool _titleFontAdjusted = false; // ensure title font sized only once consistently
    private float? _titleFontBaseSize = null; // original title font size
    private bool _pendingDarkIdleFix = false; // schedule dark idle palette enforcement

    private static Font CreateUiFont()
    {
        // Target Aptos 8pt; fallback to Segoe UI if Aptos not installed
        const float size = 8f; // previously 9f
        try { return new Font("Aptos", size, FontStyle.Regular, GraphicsUnit.Point); } catch { }
        try { return new Font("Aptos Display", size, FontStyle.Regular, GraphicsUnit.Point); } catch { }
        try { return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point); } catch { }
        return SystemFonts.DefaultFont;
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_SHOW = 5;

    public CmdKitForm()
    {
        InitializeComponent();
        _listTooltip.OwnerDraw = true;
        _listTooltip.Draw += ListTooltip_Draw;
        _listTooltip.Popup += ListTooltip_Popup;
        AdjustLayout();
        this.Resize += (_, _) => AdjustLayout();
        ApplyGlobalFont(this.Controls);
        var icoPath = Path.Combine(AppContext.BaseDirectory, "CmdKit.ico");
        if (!File.Exists(icoPath))
        {
            var subPath = Path.Combine(AppContext.BaseDirectory, "Resources", "CmdKit.ico");
            if (File.Exists(subPath)) icoPath = subPath;
        }
        if (File.Exists(icoPath)) { try { this.Icon = new Icon(icoPath); } catch { } }
        this.ShowIcon = true;
        _dataFile = Path.Combine(GetActiveDataDir(), "commands.json");
        ApplyTheme(); // initial
        if (_settings.Theme == AppTheme.Dark)
        {
            // schedule a post-initial fix on first idle to override Sunny.UI late style painting
            _pendingDarkIdleFix = true;
            Application.Idle += Application_Idle_DarkFix;
        }
        this.HandleCreated += (s, e) => { if (_settings.Theme == AppTheme.Dark) ApplyTheme(); };
        this.Shown += (s, e) => { if (_settings.Theme == AppTheme.Dark) { BeginInvoke(new Action(() => ApplyTheme())); } };
        InitTray();
        if (_trayIcon != null && this.Icon != null) _trayIcon.Icon = this.Icon;
        this.HandleCreated += (s, e) => TryRegisterHotKey();
        this.FormClosing += CmdKitForm_FormClosing;
        _tooltipTimer.Interval = 200; _tooltipTimer.Tick += TooltipTimer_Tick; _tooltipTimer.Start();
    }

    private void Application_Idle_DarkFix(object? sender, EventArgs e)
    {
        if (!_pendingDarkIdleFix) return;
        _pendingDarkIdleFix = false;
        Application.Idle -= Application_Idle_DarkFix;
        if (_settings.Theme == AppTheme.Dark)
        {
            var (bg, surface, surfaceAlt, text, border, accentActive, _) = GetTheme();
            ApplySunnyUiControlColors(bg, surface, surfaceAlt, text, border, accentActive);
            ForceDarkControlPalette(surface, surfaceAlt, text, border, accentActive);
            listEntries?.Invalidate();
        }
    }

    // Applies unified font recursively
    private void ApplyGlobalFont(Control.ControlCollection controls)
    {
        if (controls == null) return;
        foreach (Control c in controls)
        {
            try { c.Font = _uiFont; } catch { }
            if (c.HasChildren) ApplyGlobalFont(c.Controls);
        }
    }

    private void AdjustLayout()
    {
        // Ensure controls exist
        if (txtSearch == null || flowButtons == null || listEntries == null || statusStrip == null) return;
        int margin = 8;
        int top = this.TitleHeight + margin; // SunnyUI title bar height
        txtSearch.Top = top;
        btnClearSearch.Top = top;
        cmbKindFilter.Top = top;
        btnSettings.Top = top;
        // align vertically
        int belowTop = top + txtSearch.Height + margin;
        flowButtons.Top = belowTop;
        // list box below buttons
        listEntries.Top = flowButtons.Bottom + margin;
        // resize list height to leave space for status strip
        listEntries.Height = Math.Max(60, this.ClientSize.Height - listEntries.Top - statusStrip.Height - margin);
        // stretch widths
        txtSearch.Left = margin;
        txtSearch.Width = Math.Max(120, this.ClientSize.Width - 4 * margin - cmbKindFilter.Width - btnSettings.Width - btnClearSearch.Width);
        btnClearSearch.Left = txtSearch.Right + 2;
        cmbKindFilter.Left = btnClearSearch.Right + 4;
        btnSettings.Left = cmbKindFilter.Right + 4;
        flowButtons.Left = margin;
        flowButtons.Width = this.ClientSize.Width - margin * 2;
        listEntries.Left = margin;
        listEntries.Width = this.ClientSize.Width - margin * 2;
    }

    private string GetActiveDataDir() => string.IsNullOrWhiteSpace(_settings.DataPath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CmdKit")
        : _settings.DataPath;

    #region Theme
    private (Color bg, Color surface, Color surfaceAlt, Color text, Color border, Color accentActive, bool blossom) GetTheme()
    {
        bool blossom = _settings.Theme == AppTheme.Blossom;
        bool light = _settings.Theme == AppTheme.Light;
        if (blossom)
            return (ThemeColorsBlossom.Background, ThemeColorsBlossom.Surface, ThemeColorsBlossom.SurfaceAlt, ThemeColorsBlossom.TextPrimary, ThemeColorsBlossom.Border, ThemeColorsBlossom.AccentActive, true);
        if (light)
            return (ThemeColorsLight.Background, ThemeColorsLight.Surface, ThemeColorsLight.SurfaceAlt, ThemeColorsLight.TextPrimary, ThemeColorsLight.Border, ThemeColorsLight.AccentActive, false);
        return (ThemeColors.Background, ThemeColors.Surface, ThemeColors.SurfaceAlt, ThemeColors.TextPrimary, ThemeColors.Border, ThemeColors.AccentActive, false);
    }

    private void ApplyTheme()
    {
        var (bg, surface, surfaceAlt, text, border, accentActive, blossom) = GetTheme();
        bool dark = _settings.Theme == AppTheme.Dark;
        if (dark)
        {
            this.Style = Sunny.UI.UIStyle.Custom;
            this.StyleCustomMode = true;
            _styleMgr.Style = Sunny.UI.UIStyle.Gray;
        }
        else if (_settings.Theme == AppTheme.Light) _styleMgr.Style = Sunny.UI.UIStyle.Blue;
        else if (_settings.Theme == AppTheme.Blossom) _styleMgr.Style = Sunny.UI.UIStyle.Red;

        if (dark) this.BackColor = bg;
        ApplyThemeColorsToForm(this, bg, surface, surfaceAlt, text, border, accentActive, blossom);
        ApplySunnyUiControlColors(bg, surface, surfaceAlt, text, border, accentActive); // call existing overload
        if (dark) // enforce custom mode immediately
        {
            ForceDarkControlPalette(surface, surfaceAlt, text, border, accentActive);
        }
        ApplyTitleBarColors(bg, surface, surfaceAlt, text, border, accentActive, dark);
        ApplyFixedTitleFont();
        listEntries?.Invalidate();
    }

    private void ApplyFixedTitleFont()
    {
        // Force TitleFont to 8f exactly once
        if (_titleFontAdjusted) return;
        try
        {
            var f = this.TitleFont; if (f == null) return;
            const float target = 8f;
            if (Math.Abs(f.Size - target) > 0.1f)
            {
                this.TitleFont = new Font(f.FontFamily, target, f.Style, f.Unit);
            }
            this.TitleHeight = (int)Math.Ceiling(this.TitleFont.GetHeight() + 6);
            _titleFontAdjusted = true;
        }
        catch { }
    }

    private Color GetBetterHover(Color baseColor, Color textColor, bool dark)
    {
        // Adaptive hover color selection that maximizes contrast with text
        Color Clamp(int r, int g, int b) => Color.FromArgb(baseColor.A, Math.Max(0, Math.Min(255, r)), Math.Max(0, Math.Min(255, g)), Math.Max(0, Math.Min(255, b)));
        Color Lighten(int delta) => Clamp(baseColor.R + delta, baseColor.G + delta, baseColor.B + delta);
        Color Darken(int delta) => Clamp(baseColor.R - delta, baseColor.G - delta, baseColor.B - delta);

        double Lum(Color c)
        {
            static double Srgb(double ch)
            {
                ch /= 255.0; return ch <= 0.03928 ? ch / 12.92 : Math.Pow((ch + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Srgb(c.R) + 0.7152 * Srgb(c.G) + 0.0722 * Srgb(c.B);
        }
        double Contrast(Color a, Color b)
        {
            double l1 = Lum(a) + 0.05, l2 = Lum(b) + 0.05; return l1 > l2 ? l1 / l2 : l2 / l1;
        }

        // Baseline contrast to text
        double baseContrast = Contrast(baseColor, textColor);

        // Dark theme: prefer lightening slightly (too dark -> flatten) but still preserve text
        if (dark)
        {
            var lighter = Lighten(14); // moderate lift
            if (Contrast(lighter, textColor) >= baseContrast - 0.2) return lighter; // keep or improve readability
            var darker = Darken(18);
            if (Contrast(darker, textColor) >= baseContrast - 0.2) return darker;
            return baseContrast < 3.5 ? Lighten(22) : Darken(10); // fallback
        }
        else
        {
            // Light / Blossom: base background usually light; make hover noticeably darker for differentiation
            var darkerStrong = Darken(28); // strong darken
            var darkerMed = Darken(18);
            var lighterSoft = Lighten(12);
            double cStrong = Contrast(darkerStrong, textColor);
            double cMed = Contrast(darkerMed, textColor);
            double cLight = Contrast(lighterSoft, textColor);
            // Prefer darkerStrong if it keeps contrast above 3.2 (readable for small UI text)
            if (cStrong >= 3.2) return darkerStrong;
            if (cMed >= 3.2) return darkerMed;
            // If both darken attempts hurt contrast (textColor probably dark), then lighten instead
            if (cLight > baseContrast + 0.3) return lighterSoft;
            // Last resort: slight darken
            return Darken(12);
        }
    }

    private void ForceDarkControlPalette(Color surface, Color surfaceAlt, Color text, Color border, Color accent)
    {
        // For early painting (first launch) enforce fill on Sunny.UI controls to avoid initial white
        void Apply(Control root)
        {
            if (root == null) return;
            if (root.GetType().Namespace?.StartsWith("Sunny.UI") == true)
            {
                try
                {
                    var scProp = root.GetType().GetProperty("StyleCustomMode");
                    scProp?.SetValue(root, true);
                    var fill = root.GetType().GetProperty("FillColor"); fill?.SetValue(root, surface);
                    var rect = root.GetType().GetProperty("RectColor"); rect?.SetValue(root, border);
                    var fore = root.GetType().GetProperty("ForeColor"); fore?.SetValue(root, text);
                    var hoverProp = root.GetType().GetProperty("FillHoverColor");
                    if (hoverProp != null)
                    {
                        var hoverClr = GetBetterHover(surface, text, true);
                        hoverProp.SetValue(root, hoverClr);
                    }
                }
                catch { }
            }
            foreach (Control c in root.Controls) Apply(c);
        }
        Apply(this);
    }

    private void ApplyTitleBarColors(Color bg, Color surface, Color surfaceAlt, Color text, Color border, Color accent, bool dark)
    {
        try
        {
            this.StyleCustomMode = true;
            this.RectColor = border;
            this.TitleColor = surface;
            this.TitleForeColor = text;
            this.BackColor = bg;
            this.ControlBoxForeColor = text;
            var hover = GetBetterHover(surface, text, dark);
            this.ControlBoxFillHoverColor = hover;
            this.ControlBoxCloseFillHoverColor = accent;
        }
        catch { }
    }

    private void ApplySunnyUiControlColors(Color bg, Color surface, Color surfaceAlt, Color text, Color border, Color accent)
    {
        ApplyGlobalFont(this.Controls);
        bool dark = _settings.Theme == AppTheme.Dark;
        if (txtSearch != null)
        {
            if (dark) txtSearch.StyleCustomMode = true;
            txtSearch.FillColor = surface; txtSearch.RectColor = border; txtSearch.ForeColor = text; if (string.IsNullOrEmpty(txtSearch.Text)) txtSearch.Watermark = "Search..."; txtSearch.Font = _uiFont; 
        }
        if (btnClearSearch != null)
        {
            if (dark) btnClearSearch.StyleCustomMode = true;
            btnClearSearch.FillColor = surface; btnClearSearch.RectColor = surface; btnClearSearch.ForeColor = text; btnClearSearch.FillHoverColor = surfaceAlt; btnClearSearch.FillPressColor = accent; btnClearSearch.Font = _uiFont;
        }
        if (cmbKindFilter != null)
        {
            if (dark) cmbKindFilter.StyleCustomMode = true;
            cmbKindFilter.FillColor = surface; cmbKindFilter.RectColor = border; cmbKindFilter.ForeColor = text; cmbKindFilter.DropDownStyle = Sunny.UI.UIDropDownStyle.DropDownList; cmbKindFilter.Font = _uiFont;
        }
        if (btnSettings != null)
        {
            if (dark) btnSettings.StyleCustomMode = true;
            btnSettings.FillColor = surface; btnSettings.RectColor = surface; btnSettings.ForeColor = text; btnSettings.FillHoverColor = surfaceAlt; btnSettings.FillPressColor = accent; btnSettings.Font = _uiFont;
        }
        foreach (var b in new[] { btnAdd, btnCopy, btnImport, btnExport })
        {
            if (b == null) continue; if (dark) b.StyleCustomMode = true; b.FillColor = surface; b.RectColor = border; b.ForeColor = text; b.FillHoverColor = surfaceAlt; b.FillPressColor = accent; b.RectHoverColor = border; b.RectPressColor = accent; b.Font = _uiFont;
        }
        if (listEntries != null)
        {
            if (dark) listEntries.StyleCustomMode = true;
            listEntries.FillColor = surface; listEntries.RectColor = border; listEntries.ForeColor = text; listEntries.ItemSelectBackColor = accent; listEntries.ItemSelectForeColor = Color.White; listEntries.Font = _uiFont;
            try { listEntries.HoverColor = GetBetterHover(surface, text, dark); } catch { }
        }
        if (flowButtons != null) flowButtons.BackColor = bg;
        if (statusStrip != null) statusStrip.BackColor = surface;
        if (lblStatus != null) { lblStatus.ForeColor = text; lblStatus.Font = _uiFont; }
        this.BackColor = bg;
    }

    internal void ApplyThemeToExternalForm(Form f)
    {
        var (bg, surface, surfaceAlt, text, border, accentActive, blossom) = GetTheme();
        ApplyThemeColorsToForm(f, bg, surface, surfaceAlt, text, border, accentActive, blossom: false); // disable blossom gradient for dialogs
    }

    private void ApplyThemeColorsToForm(Form f, Color bg, Color surface, Color surfaceAlt, Color text, Color border, Color accentActive, bool blossom)
    {
        f.BackColor = bg; f.ForeColor = text;
        foreach (Control c in f.Controls) StyleControlRecursiveTheme(c, bg, surface, surfaceAlt, text, border, accentActive);
        if (f == this)
        {
            if (flowButtons != null) flowButtons.BackColor = bg;
            if (statusStrip != null) statusStrip.BackColor = surface;
            if (lblStatus != null) lblStatus.ForeColor = text;
            if (listEntries != null) { listEntries.BackColor = surface; listEntries.ForeColor = text; }
            if (blossom)
            {
                this.Paint -= CmdKitForm_PaintBlossom; this.Paint += CmdKitForm_PaintBlossom; this.Invalidate();
            }
            else this.Paint -= CmdKitForm_PaintBlossom;
        }
    }

    private void CmdKitForm_PaintBlossom(object? sender, PaintEventArgs e)
    {
        var r = this.ClientRectangle; if (r.Width == 0 || r.Height == 0) return;
        using var lg = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(40, 231, 84, 128), Color.FromArgb(10, 255, 255, 255), 45f);
        e.Graphics.FillRectangle(lg, r);
    }

    private void StyleControlRecursiveTheme(Control c, Color bg, Color surface, Color surfaceAlt, Color text, Color border, Color accentActive)
    {
        switch (c)
        {
            case Button b:
                b.BackColor = surface; b.ForeColor = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = border; b.FlatAppearance.MouseOverBackColor = surfaceAlt; b.FlatAppearance.MouseDownBackColor = accentActive; break;
            case TextBox tb:
                tb.BackColor = surface; tb.ForeColor = text; tb.BorderStyle = BorderStyle.FixedSingle; break;
            case ComboBox cb:
                cb.BackColor = surface; cb.ForeColor = text; cb.FlatStyle = FlatStyle.Flat; break;
            case FlowLayoutPanel or Panel:
                c.BackColor = bg; break;
        }
        foreach (Control child in c.Controls) StyleControlRecursiveTheme(child, bg, surface, surfaceAlt, text, border, accentActive);
    }
    #endregion

    private void CmdKitForm_Load(object sender, EventArgs e)
    {
        LoadData();
        ApplyFilter();
    }

    private void LoadData()
    {
        try
        {
            if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
            if (File.Exists(_dataFile))
            {
                string json = File.ReadAllText(_dataFile);
                var list = JsonSerializer.Deserialize<List<CommandEntry>>(json);
                _all = list ?? new List<CommandEntry>();
            }
            else _all = new List<CommandEntry>();
        }
        catch (Exception ex) { MessageBox.Show("Load data failed: " + ex.Message); }

        _kinds.Clear();
        foreach (var k in _all.Select(a => a.Kind).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase)) _kinds.Add(k!);
        RebuildKindFilterItems();
    }

    private void SaveData()
    {
        try
        {
            if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
            foreach (var e in _all)
            {
                if (!e.IsEncrypted && ShouldEncrypt(e) && !string.IsNullOrEmpty(e.Value))
                    e.Value = SecretProtector.Protect(e.Value);
            }
            string json = JsonSerializer.Serialize(_all, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_dataFile, json);
        }
        catch (Exception ex) { MessageBox.Show("Save data failed: " + ex.Message); }
    }

    private void RebuildKindFilterItems()
    {
        if (cmbKindFilter == null) return;
        var prev = cmbKindFilter.SelectedItem?.ToString();
        cmbKindFilter.Items.Clear();
        cmbKindFilter.Items.Add("All");
        foreach (var k in _kinds.OrderBy(x => x)) cmbKindFilter.Items.Add(k);
        if (!string.IsNullOrEmpty(prev))
        {
            var idx = cmbKindFilter.Items.IndexOf(prev);
            cmbKindFilter.SelectedIndex = idx >= 0 ? idx : 0;
        }
        else cmbKindFilter.SelectedIndex = 0;
    }

    private void ScheduleFilter()
    {
        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(180, cts.Token);
                if (cts.IsCancellationRequested) return;
                var snapshot = _all.ToArray();
                string kw = string.Empty;
                Invoke(new Action(() => kw = txtSearch.Text?.Trim() ?? string.Empty));
                int kindIndex = -1; string kindText = string.Empty;
                Invoke(new Action(() => { if (cmbKindFilter != null) { kindIndex = cmbKindFilter.SelectedIndex; kindText = cmbKindFilter.SelectedItem?.ToString() ?? ""; } }));
                IEnumerable<CommandEntry> data = snapshot;
                if (kindIndex > 0 && !string.IsNullOrEmpty(kindText)) data = data.Where(x => string.Equals(x.Kind, kindText, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(kw)) data = data.Where(x => x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) || (x.Description ?? string.Empty).Contains(kw, StringComparison.OrdinalIgnoreCase));
                var names = data.OrderByDescending(x => x.UpdatedUtc).Select(x => x.Name).ToList();
                if (cts.IsCancellationRequested) return;
                Invoke(new Action(() =>
                {
                    listEntries.BeginUpdate();
                    listEntries.Items.Clear();
                    foreach (var n in names) listEntries.Items.Add(n);
                    listEntries.EndUpdate();
                    UpdateStatus($"Total: {names.Count}");
                }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { try { Invoke(new Action(() => UpdateStatus("Filter error: " + ex.Message))); } catch { } }
        });
    }

    private void ApplyFilter() => ScheduleFilter();

    private CommandEntry? GetSelected()
    {
        if (listEntries.SelectedItem == null) return null;
        string name = listEntries.SelectedItem.ToString()!;
        return _all.FirstOrDefault(x => x.Name == name);
    }

    private void CopySelected()
    {
        var sel = GetSelected(); if (sel == null) { UpdateStatus("No selection."); return; }
        try
        {
            string value = sel.Value;
            if (sel.IsEncrypted) value = SecretProtector.Unprotect(value);
            Clipboard.SetText(value);
            UpdateStatus($"Copied: {sel.Name}");
            if (_settings.AutoCloseAfterCopy) this.Hide();
        }
        catch (Exception ex) { MessageBox.Show("Copy failed: " + ex.Message); UpdateStatus("Copy failed."); }
    }

    private void listEntries_DoubleClick(object sender, EventArgs e) => CopySelected();
    private void listEntries_KeyDown(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.C)) { CopySelected(); e.Handled = true; } }
    private void listEntries_MouseMove(object sender, MouseEventArgs e)
    { 
        // UIListBox does not derive from ListBox; compute index manually
        if (listEntries == null) return;
        int itemHeight = listEntries.ItemHeight; // default property
        if (itemHeight <= 0) itemHeight = (int)_uiFont.GetHeight() + 6;
        int index = (e.Y / itemHeight);
        if (index < 0 || index >= listEntries.Items.Count) index = -1;
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            _lastToolTipTime = DateTime.MinValue;
        }
    }
    private void listEntries_MouseLeave(object? sender, EventArgs e) { _hoverIndex = -1; _listTooltip.Hide(listEntries); }
    private void ListTooltip_Popup(object? sender, PopupEventArgs e)
    {
        // Recalculate size with chosen font
        var text = e.ToolTipSize; // struct copy
        string tip = (sender as ToolTip)?.GetToolTip(listEntries) ?? string.Empty;
        var font = GetTooltipFont(tip);
        var sz = TextRenderer.MeasureText(tip, font, new Size(600, 400), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        e.ToolTipSize = new Size(Math.Min(600, sz.Width + 12), Math.Min(600, sz.Height + 8));
    }

    private void ListTooltip_Draw(object? sender, DrawToolTipEventArgs e)
    {
        string tip = e.ToolTipText;
        var font = GetTooltipFont(tip);
        e.Graphics.Clear(Color.FromArgb(255, 32, 32, 32));
        using var borderPen = new Pen(Color.FromArgb(90, 200, 200, 200));
        e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(235, 24, 24, 24)), e.Bounds);
        e.Graphics.DrawRectangle(borderPen, new Rectangle(0,0,e.Bounds.Width-1,e.Bounds.Height-1));
        TextRenderer.DrawText(e.Graphics, tip, font, new Rectangle(6,4,e.Bounds.Width-12,e.Bounds.Height-8), Color.White, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
    }

    private Font GetTooltipFont(string text)
    {
        // if contains CJK range chars, use YaHei font
        if (text.Any(c => c > 0x3000))
        {
            _tooltipFontCjk ??= CreateCjkFont(_uiFont.Size);
            return _tooltipFontCjk!;
        }
        return _uiFont;
    }

    private static Font CreateCjkFont(float size)
    {
        string[] fonts = { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimSun" };
        foreach (var f in fonts)
        {
            try { return new Font(f, size, FontStyle.Regular, GraphicsUnit.Point); } catch { }
        }
        return SystemFonts.DefaultFont;
    }

    private void TooltipTimer_Tick(object? sender, EventArgs e)
    {
        if (listEntries == null) return;
        if (_hoverIndex < 0 || _hoverIndex >= listEntries.Items.Count) { _listTooltip.Hide(listEntries); return; }
        // Reduce frequency
        if ((DateTime.Now - _lastToolTipTime).TotalMilliseconds < 300) return;
        var name = listEntries.Items[_hoverIndex]?.ToString();
        if (string.IsNullOrEmpty(name)) return;
        var entry = _all.FirstOrDefault(x => x.Name == name);
        if (entry == null) return;
        string tip;
        if (entry.IsEncrypted)
        {
            try
            {
                if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                {
                    tip = SecretProtector.Unprotect(entry.Value);
                }
                else
                {
                    var plain = SecretProtector.Unprotect(entry.Value);
                    tip = entry.Name + " (secret)\n" + new string('*', Math.Min(plain.Length, 6)) + "  (Hold Shift to reveal)"; // updated hint to English
                }
            }
            catch { tip = entry.Name + " (secret)"; }
        }
        else tip = entry.Value;
        var clientPos = listEntries.PointToClient(Cursor.Position);
        _listTooltip.Show(tip, listEntries, Math.Min(clientPos.X + 24, listEntries.Width - 60), clientPos.Y + 24, 4000);
        _lastToolTipTime = DateTime.Now;
    }
    private void listEntries_MouseDown(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Right) { int index = listEntries.IndexFromPoint(e.Location); if (index >= 0 && index < listEntries.Items.Count) listEntries.SelectedIndex = index; } }

    private void cmbKindFilter_SelectedIndexChanged(object sender, EventArgs e) => ApplyFilter();
    private void txtSearch_TextChanged(object sender, EventArgs e)
    { ApplyFilter(); if (btnClearSearch != null) btnClearSearch.Visible = !string.IsNullOrEmpty(txtSearch.Text); }
    private void btnAdd_Click(object sender, EventArgs e)
    { using var form = new AddEditCommandForm(); form.Owner = this; if (form.ShowDialog(this) == DialogResult.OK && form.Entry != null) { form.Entry.CreatedUtc = form.Entry.UpdatedUtc = DateTime.UtcNow; _all.Add(form.Entry); SaveData(); ApplyFilter(); if (!string.IsNullOrWhiteSpace(form.Entry.Kind)) { _kinds.Add(form.Entry.Kind); RebuildKindFilterItems(); } } }
    private void btnEdit_Click(object sender, EventArgs e)
    { var sel = GetSelected(); if (sel == null) return; using var form = new AddEditCommandForm(sel); form.Owner = this; if (form.ShowDialog(this) == DialogResult.OK && form.Entry != null) { sel.Name = form.Entry.Name; sel.Value = form.Entry.Value; sel.Description = form.Entry.Description; sel.Kind = form.Entry.Kind; sel.UpdatedUtc = DateTime.UtcNow; SaveData(); ApplyFilter(); if (!string.IsNullOrWhiteSpace(sel.Kind)) { _kinds.Add(sel.Kind); RebuildKindFilterItems(); } } }
    private void btnDelete_Click(object sender, EventArgs e)
    { var sel = GetSelected(); if (sel == null) return; if (MessageBox.Show("Delete this item: " + sel.Name + "?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes) { _all.Remove(sel); SaveData(); ApplyFilter(); } }
    private void btnCopy_Click(object sender, EventArgs e) => CopySelected();
    private void miCopy_Click(object sender, EventArgs e) => CopySelected();
    private void miEdit_Click(object sender, EventArgs e) => btnEdit_Click(sender, e);
    private void miDelete_Click(object sender, EventArgs e) => btnDelete_Click(sender, e);

    private void btnImport_Click(object sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "JSON Files (*.json)|*.json" };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                string json = File.ReadAllText(ofd.FileName);
                var list = JsonSerializer.Deserialize<List<CommandEntry>>(json) ?? new List<CommandEntry>();
                int reEncrypted = 0;
                foreach (var item in list)
                {
                    if (item.WasEncrypted == true && !item.IsEncrypted && !string.IsNullOrEmpty(item.Value)) { try { item.Value = SecretProtector.Protect(item.Value); reEncrypted++; } catch { } item.WasEncrypted = null; }
                    var exist = _all.FirstOrDefault(x => x.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
                    if (exist == null) _all.Add(item);
                    else { exist.Value = item.Value; exist.Description = item.Description; exist.Kind = item.Kind; exist.UpdatedUtc = DateTime.UtcNow; }
                    if (!string.IsNullOrWhiteSpace(item.Kind)) _kinds.Add(item.Kind);
                }
                foreach (var entry in _all) if (!entry.IsEncrypted && ShouldEncrypt(entry)) entry.Value = SecretProtector.Protect(entry.Value);
                RebuildKindFilterItems();
                SaveData(); ApplyFilter();
                MessageBox.Show($"Import completed. Auto re-encrypted: {reEncrypted}");
            }
            catch (Exception ex) { MessageBox.Show("Import failed: " + ex.Message); }
        }
    }

    private void btnExport_Click(object sender, EventArgs e)
    {
        using var sfd = new SaveFileDialog { Filter = "JSON Files (*.json)|*.json", FileName = "commands_export.json" };
        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                var exportList = _all.Select(e =>
                {
                    if (e.IsEncrypted)
                    {
                        try { var plain = SecretProtector.Unprotect(e.Value); return new CommandEntry { Id = e.Id, Name = e.Name, Description = e.Description, Kind = e.Kind, CreatedUtc = e.CreatedUtc, UpdatedUtc = e.UpdatedUtc, Value = plain, WasEncrypted = true }; }
                        catch { return e; }
                    }
                    return e;
                }).ToList();
                string json = JsonSerializer.Serialize(exportList, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sfd.FileName, json);
                MessageBox.Show("Export completed (plaintext for portability). Encrypted items tagged with WasEncrypted=true.");
            }
            catch (Exception ex) { MessageBox.Show("Export failed: " + ex.Message); }
        }
    }

    private void btnReload_Click(object sender, EventArgs e) { }

    private void UpdateStatus(string text) { this.Text = "CmdKit - " + text; if (lblStatus != null) lblStatus.Text = text; }

    private void btnSettings_Click(object sender, EventArgs e)
    { using var dlg = new SettingsForm(_settings); dlg.Owner = this; if (dlg.ShowDialog(this) == DialogResult.OK) { _settings.Save(); _dataFile = Path.Combine(GetActiveDataDir(), "commands.json"); LoadData(); ApplyFilter(); ApplyTheme(); if (_trayIcon != null && this.Icon != null) _trayIcon.Icon = this.Icon; } }

    private void InitTray()
    {
        _trayIcon = new NotifyIcon { Icon = this.Icon ?? SystemIcons.Application, Text = "CmdKit (Ctrl+Q)", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _trayIcon.ContextMenuStrip.Items.Add("Show", null, (s, e) => ShowAndActivate());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e) => { _allowExit = true; UnregisterAndDispose(); Application.Exit(); });
        _trayIcon.DoubleClick += (s, e) => ShowAndActivate();
    }
    private void TryRegisterHotKey() { try { UnregisterHotKey(this.Handle, HOTKEY_ID); RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL, (int)Keys.Q); } catch (Exception ex) { UpdateStatus("Hotkey error: " + ex.Message); } }
    private void ShowAndActivate() { if (this.WindowState == FormWindowState.Minimized) this.WindowState = FormWindowState.Normal; if (!this.Visible) { ApplyTheme(); this.Show(); } else ApplyTheme(); this.Activate(); txtSearch.Focus(); }
    public void BringToFrontExternal()
    {
        try
        {
            if (this.IsDisposed) return;
            if (this.WindowState == FormWindowState.Minimized) this.WindowState = FormWindowState.Normal;
            if (!this.Visible) this.Show();
            var handle = this.Handle;
            bool prevTop = this.TopMost; this.TopMost = true; this.TopMost = prevTop;
            ShowWindow(handle, SW_SHOW); this.Activate(); SetForegroundWindow(handle); this.BringToFront(); txtSearch.Focus();
        }
        catch { }
    }
    private void ToggleVisibility() { if (this.Visible && this.WindowState != FormWindowState.Minimized) this.Hide(); else BringToFrontExternal(); }
    protected override void WndProc(ref Message m)
    { if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID) { ToggleVisibility(); return; } else if (m.Msg == (int)Program.WM_SHOWAPP) { BringToFrontExternal(); return; } base.WndProc(ref m); }
    private void CmdKitForm_FormClosing(object? sender, FormClosingEventArgs e) { if (!_allowExit) { e.Cancel = true; this.Hide(); } else { UnregisterAndDispose(); } }
    private void UnregisterAndDispose() { try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { } if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; } }

    #region SettingsForm
    private class SettingsForm : Form
    {
        private readonly AppSettings _settings; private TextBox txtPath = new(); private Button btnBrowse = new(); private ComboBox cmbTheme = new(); private Button btnOk = new(); private Button btnCancel = new(); private CheckBox chkAutoClose = new();
        public SettingsForm(AppSettings settings)
        {
            _settings = settings; Text = "Settings"; Width = 420; Height = 260; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            int left = 20; int top = 20; int labelW = 110; int gap = 32; int inputLeft = left + labelW + 6; int inputW = 230;
            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CmdKit");
            string initialPath = string.IsNullOrWhiteSpace(_settings.DataPath) ? defaultPath : _settings.DataPath;
            Controls.Add(MakeLabel("Data Folder", left, top)); txtPath.SetBounds(inputLeft, top - 2, inputW, 23); txtPath.Text = initialPath; Controls.Add(txtPath); btnBrowse.Text = "..."; btnBrowse.SetBounds(inputLeft + inputW + 5, top - 2, 30, 23); btnBrowse.Click += Browse; Controls.Add(btnBrowse); top += gap;
            Controls.Add(MakeLabel("Theme", left, top)); cmbTheme.SetBounds(inputLeft, top - 2, 160, 23); cmbTheme.DropDownStyle = ComboBoxStyle.DropDownList; cmbTheme.Items.AddRange(new object[] { AppTheme.Dark.ToString(), AppTheme.Light.ToString(), AppTheme.Blossom.ToString() }); cmbTheme.SelectedItem = _settings.Theme.ToString(); Controls.Add(cmbTheme); top += gap;
            chkAutoClose.Text = "Auto close after copy"; chkAutoClose.SetBounds(inputLeft, top - 6, 200, 24); chkAutoClose.Checked = _settings.AutoCloseAfterCopy; Controls.Add(chkAutoClose); top += gap;
            btnOk.Text = "OK"; btnOk.SetBounds(inputLeft + 40, top, 80, 30); btnOk.Click += (s, e) => { Apply(); DialogResult = DialogResult.OK; };
            btnCancel.Text = "Cancel"; btnCancel.SetBounds(inputLeft + 130, top, 80, 30); btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel; Controls.Add(btnOk); Controls.Add(btnCancel);
        }
        private void Browse(object? sender, EventArgs e) { using var f = new FolderBrowserDialog(); if (f.ShowDialog(this) == DialogResult.OK) txtPath.Text = f.SelectedPath; }
        private void Apply() { _settings.DataPath = txtPath.Text.Trim(); if (Enum.TryParse<AppTheme>(cmbTheme.SelectedItem?.ToString(), out var theme)) _settings.Theme = theme; _settings.AutoCloseAfterCopy = chkAutoClose.Checked; }
        private static Label MakeLabel(string text, int left, int top) => new() { Text = text, Left = left, Top = top, Width = 110, TextAlign = ContentAlignment.MiddleRight };
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); (Owner as CmdKitForm)?.ApplyThemeToExternalForm(this); }
    }
    #endregion

    #region AddEditCommandForm
    private class AddEditCommandForm : Form
    {
        public CommandEntry? Entry { get; private set; }
        private readonly TextBox txtName = new();
        private readonly TextBox txtValue = new();
        private readonly TextBox txtDesc = new();
        private readonly ComboBox cmbKind = new();
        private readonly CheckBox chkSecret = new();
        private readonly Button btnToggleView = new();
        private bool valueVisible = true;
        private readonly Button btnOk = new();
        private readonly Button btnCancel = new();
        public AddEditCommandForm(CommandEntry? existing = null)
        {
            Entry = existing == null ? new CommandEntry() : new CommandEntry { Id = existing.Id, Name = existing.Name, Value = existing.Value, Description = existing.Description, Kind = existing.Kind, CreatedUtc = existing.CreatedUtc, UpdatedUtc = existing.UpdatedUtc };
            Text = existing == null ? "Add Entry" : "Edit Entry"; Width = 480; Height = 420; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            txtValue.Multiline = true; txtValue.Height = 70; txtValue.ScrollBars = ScrollBars.Vertical; txtDesc.Multiline = true; txtDesc.Height = 70; txtDesc.ScrollBars = ScrollBars.Vertical;
            int left = 20; int top = 20; int lblw = 85; int gap = 32; int inputLeft = left + lblw + 6; int inputW = 300; int toggleW = 30;
            Controls.Add(MakeLabel("Name", left, top)); txtName.SetBounds(inputLeft, top - 2, inputW + toggleW + 4, 23); txtName.Text = Entry!.Name; Controls.Add(txtName); top += gap;
            Controls.Add(MakeLabel("Value", left, top)); txtValue.SetBounds(inputLeft, top - 2, inputW, 70); Controls.Add(txtValue);
            btnToggleView.Text = string.Empty; btnToggleView.SetBounds(inputLeft + inputW + 6, top - 2, toggleW, 28); btnToggleView.FlatStyle = FlatStyle.Flat; btnToggleView.FlatAppearance.BorderSize = 0; var toggleBase = txtValue.BackColor; var toggleHover = ControlPaint.Light(toggleBase, .15f); btnToggleView.BackColor = toggleBase; btnToggleView.TabStop = false; btnToggleView.Click += (s, e) => ToggleView(); btnToggleView.Paint += BtnToggleView_Paint; btnToggleView.MouseEnter += (s, e) => btnToggleView.BackColor = toggleHover; btnToggleView.MouseLeave += (s, e) => btnToggleView.BackColor = toggleBase; Controls.Add(btnToggleView); top += 78;
            Controls.Add(MakeLabel("Description", left, top)); txtDesc.SetBounds(inputLeft, top - 2, inputW + toggleW + 4, 70); txtDesc.Text = Entry.Description; Controls.Add(txtDesc); top += 78;
            Controls.Add(MakeLabel("Type", left, top)); cmbKind.SetBounds(inputLeft, top - 2, 200, 23); cmbKind.DropDownStyle = ComboBoxStyle.DropDown; cmbKind.AutoCompleteMode = AutoCompleteMode.SuggestAppend; cmbKind.AutoCompleteSource = AutoCompleteSource.CustomSource; Controls.Add(cmbKind); top += gap;
            chkSecret.Text = "Encrypt"; chkSecret.SetBounds(inputLeft, top - 4, 80, 24); chkSecret.Checked = Entry.IsEncrypted || ((CmdKitForm?)Owner)?.ShouldEncrypt(Entry!) == true; Controls.Add(chkSecret); top += gap;
            btnOk.Text = "OK"; btnOk.SetBounds(inputLeft + 60, top, 90, 32); btnOk.Click += (s, e) => OnOk();
            btnCancel.Text = "Cancel"; btnCancel.SetBounds(inputLeft + 160, top, 90, 32); btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel; Controls.AddRange(new Control[] { btnOk, btnCancel });
        }
        private static Label MakeLabel(string t, int l, int tp) => new() { Text = t, Left = l, Top = tp, Width = 85, TextAlign = ContentAlignment.MiddleRight };
        private void BtnToggleView_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; var w = btnToggleView.ClientSize.Width; var h = btnToggleView.ClientSize.Height; var pad = 6; var eyeRect = new Rectangle(pad, pad, w - pad * 2, h - pad * 2); using var pen = new Pen(ForeColor, 1.8f); var arcRect = new Rectangle(eyeRect.X, eyeRect.Y + eyeRect.Height / 4, eyeRect.Width, eyeRect.Height / 2); g.DrawArc(pen, arcRect, 180, 180); g.DrawArc(pen, arcRect, 0, 180); int pupil = Math.Max(4, eyeRect.Height / 3); var pupilRect = new Rectangle(eyeRect.X + (eyeRect.Width - pupil) / 2, eyeRect.Y + (eyeRect.Height - pupil) / 2, pupil, pupil); if (valueVisible) { using var br = new SolidBrush(ForeColor); g.FillEllipse(br, pupilRect); } else { g.DrawEllipse(pen, pupilRect); g.DrawLine(pen, eyeRect.Right - 2, eyeRect.Top + 2, eyeRect.Left + 2, eyeRect.Bottom - 2); } }
        private void ToggleView() { valueVisible = !valueVisible; if (valueVisible) txtValue.PasswordChar = '\0'; else if (txtValue.PasswordChar == '\0') txtValue.PasswordChar = '•'; btnToggleView.Invalidate(); }
        private void OnOk()
        {
            if (string.IsNullOrWhiteSpace(txtName.Text) || string.IsNullOrWhiteSpace(txtValue.Text)) { MessageBox.Show("Name and Value are required."); return; }
            Entry!.Name = txtName.Text.Trim(); var plain = txtValue.Text.Trim(); Entry.Description = string.IsNullOrWhiteSpace(txtDesc.Text) ? null : txtDesc.Text.Trim(); Entry.Kind = string.IsNullOrWhiteSpace(cmbKind.Text) ? "Command" : cmbKind.Text.Trim(); if (chkSecret.Checked || ((CmdKitForm?)Owner)?.ShouldEncrypt(Entry) == true) Entry.Value = SecretProtector.Protect(plain); else Entry.Value = plain; Entry.UpdatedUtc = DateTime.UtcNow; DialogResult = DialogResult.OK;
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            PopulateKinds();
            if (string.IsNullOrWhiteSpace(cmbKind.Text)) cmbKind.Text = Entry?.Kind ?? "Command"; // ensure text
            (Owner as CmdKitForm)?.ApplyThemeToExternalForm(this);
            if (Entry!.IsEncrypted) { txtValue.Text = SecretProtector.Unprotect(Entry.Value); } else txtValue.Text = Entry.Value;
        }

        private void PopulateKinds()
        {
            try
            {
                var defaults = new[] { "Command", "Link", "URL", "Password" };
                HashSet<string> kinds = new(StringComparer.OrdinalIgnoreCase);
                foreach (var d in defaults) kinds.Add(d);
                if (Owner is CmdKitForm host)
                {
                    foreach (var k in host._kinds) kinds.Add(k);
                }
                if (!string.IsNullOrWhiteSpace(Entry?.Kind)) kinds.Add(Entry.Kind);
                var ordered = kinds.OrderBy(k => k).ToArray();
                cmbKind.BeginUpdate();
                cmbKind.Items.Clear();
                cmbKind.Items.AddRange(ordered);
                var ac = new AutoCompleteStringCollection(); ac.AddRange(ordered); cmbKind.AutoCompleteCustomSource = ac;
                cmbKind.EndUpdate();
            }
            catch { }
        }
    }
    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _filterCts?.Cancel(); _filterCts?.Dispose(); _listTooltip?.Dispose(); _trayIcon?.Dispose(); _tooltipTimer?.Stop(); _tooltipTimer?.Dispose();
            // removed _titleFontOverride disposal (no longer used)
        }
        base.Dispose(disposing);
    }

    private bool MatchesSensitive(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (_settings.SensitivePatterns == null || _settings.SensitivePatterns.Count == 0) return false;
        foreach (var p in _settings.SensitivePatterns) { try { if (Regex.IsMatch(text, p, RegexOptions.IgnoreCase)) return true; } catch { } }
        return false;
    }
    private bool ShouldEncrypt(CommandEntry e) => MatchesSensitive(e.Name) || MatchesSensitive(e.Kind) || MatchesSensitive(e.Description ?? string.Empty);
}
