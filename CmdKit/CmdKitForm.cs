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
    private GlobalHotkeyHost? _hotkeyHost; // persistent hidden window for global hotkey
    private CancellationTokenSource? _filterCts;
    internal readonly HashSet<string> _kinds = new(StringComparer.OrdinalIgnoreCase) { "Command", "Link", "URL", "Password" };
    private readonly System.Windows.Forms.Timer _tooltipTimer = new();
    private readonly System.Windows.Forms.Timer _searchEnforceTimer = new();
    private int _hoverIndex = -1;
    private Font _uiFont; // created in ctor using settings size
    private Font? _tooltipFontCjk; // fallback for CJK
    private bool _pendingDarkIdleFix = false; // schedule dark idle palette enforcement
    private readonly HashSet<int> _multiSelected = new();
    private int _multiAnchor = -1;
    private Color _currentAccent = Color.SteelBlue; // updated by ApplyTheme
    private Color _multiSelectFill = Color.FromArgb(200, 70, 130, 180);
    private Color _multiSelectBorder = Color.FromArgb(255, 255, 255, 255);
    private bool _isDarkTheme = true;
    private bool _titleFontAdjusted = false; // added back for ApplyFixedTitleFont

    private static Font CreateUiFont(float size = 10f)
    {
        try { return new Font("Aptos", size, FontStyle.Regular, GraphicsUnit.Point); } catch { }
        try { return new Font("Aptos Display", size, FontStyle.Regular, GraphicsUnit.Point); } catch { }
        try { return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point); } catch { }
        return SystemFonts.DefaultFont;
    }

    // Helper to set Sunny.UI reflection properties safely
    private void TrySetProp(object target, string propName, object? value)
    { try { var p = target.GetType().GetProperty(propName); p?.SetValue(target, value); } catch { } }

    // Force inner native TextBox inside Sunny.UI.UITextBox to adopt our dark theme colors
    private void FixSearchInnerColors()
    {
        if (txtSearch == null) return;
        Color back = txtSearch.FillColor;
        Color fore = txtSearch.ForeColor == Color.Empty ? Color.White : txtSearch.ForeColor;
        try
        {
            // Force all relevant state colors on the Sunny.UI UITextBox itself
            TrySetProp(txtSearch, "FillHoverColor", back);
            TrySetProp(txtSearch, "FillPressColor", back);
            TrySetProp(txtSearch, "FillSelectedColor", back);
            TrySetProp(txtSearch, "FillFocusColor", back); // if exists
            TrySetProp(txtSearch, "RectHoverColor", txtSearch.RectColor);
            TrySetProp(txtSearch, "RectFocusColor", txtSearch.RectColor);
            foreach (Control c in txtSearch.Controls)
            {
                if (c is TextBox inner)
                {
                    inner.BackColor = back;
                    inner.ForeColor = fore;
                    inner.BorderStyle = BorderStyle.None;
                    inner.Cursor = Cursors.IBeam;
                }
            }
        }
        catch { }
    }

    private void ForceSearchColorsDark()
    {
        if (_settings.Theme != AppTheme.Dark || txtSearch == null) return;
        var (_, surface, _, text, border, _, _) = GetTheme();
        txtSearch.StyleCustomMode = true;
        txtSearch.FillColor = surface;
        TrySetProp(txtSearch, "FillColor2", surface);
        TrySetProp(txtSearch, "FillHoverColor", surface);
        TrySetProp(txtSearch, "FillPressColor", surface);
        TrySetProp(txtSearch, "FillFocusColor", surface);
        TrySetProp(txtSearch, "FillSelectedColor", surface);
        txtSearch.RectColor = border;
        TrySetProp(txtSearch, "RectHoverColor", border);
        TrySetProp(txtSearch, "RectFocusColor", border);
        txtSearch.ForeColor = text;
        FixSearchInnerColors();
    }

    public CmdKitForm()
    {
        InitializeComponent();
        _uiFont = CreateUiFont(_settings.UiFontSize);
        // enable window resizing (Sunny.UI.UIForm specific + standard)
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true; this.MinimizeBox = true;
        try { this.ShowDragStretch = true; } catch { } // Sunny.UI specific (ignore if property missing)
        this.MinimumSize = new Size(480, 250);
        _dataFile = Path.Combine(GetActiveDataDir(), "commands.json");
        // create persistent hotkey host (independent of form handle)
        _hotkeyHost = new GlobalHotkeyHost(this);
        _listTooltip.OwnerDraw = true;
        _listTooltip.Draw += ListTooltip_Draw;
        _listTooltip.Popup += ListTooltip_Popup;
        // Enable double buffering on the list control (Sunny.UI.UIListBox) to reduce scroll flicker
        TryEnableDoubleBuffer(listEntries);
        AdjustLayout();
        this.Resize += (_, _) => AdjustLayout();
        ApplyGlobalFont(this.Controls);
        // configure search enforce timer
        _searchEnforceTimer.Interval = 120; // frequent enough to beat library repaint
        _searchEnforceTimer.Tick += (s, e) =>
        {
            if (_settings.Theme == AppTheme.Dark && txtSearch != null && (txtSearch.Focused || txtSearch.ClientRectangle.Contains(txtSearch.PointToClient(Cursor.Position))))
            {
                ForceSearchColorsDark();
            }
            else if (_searchEnforceTimer.Enabled && (txtSearch == null || (!txtSearch.Focused && !txtSearch.ClientRectangle.Contains(txtSearch.PointToClient(Cursor.Position)))))
            {
                _searchEnforceTimer.Stop();
            }
        };
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
        this.Invalidate();
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
        _currentAccent = accentActive;
        _isDarkTheme = _settings.Theme == AppTheme.Dark;
        // derive multi-select colors for better contrast
        _multiSelectFill = BuildMultiSelectFill(accentActive, surface, text, _isDarkTheme);
        _multiSelectBorder = ChooseBorder(_multiSelectFill, surface, text);
        bool dark = _settings.Theme == AppTheme.Dark;

        // Configure Sunny.UI base style
        if (dark)
        {
            this.Style = Sunny.UI.UIStyle.Custom; // we will custom paint
            this.StyleCustomMode = true;
            _styleMgr.Style = Sunny.UI.UIStyle.Gray; // base neutral for dark
        }
        else if (_settings.Theme == AppTheme.Light)
        {
            _styleMgr.Style = Sunny.UI.UIStyle.Blue; // let library handle accents
            this.Style = Sunny.UI.UIStyle.Blue;
            this.StyleCustomMode = false;
            ResetSunnyUiCustomMode(this);
        }
        else if (_settings.Theme == AppTheme.Blossom)
        {
            _styleMgr.Style = Sunny.UI.UIStyle.Red;
            this.Style = Sunny.UI.UIStyle.Red;
            this.StyleCustomMode = false;
            ResetSunnyUiCustomMode(this);
        }

        if (dark) this.BackColor = bg; else this.BackColor = bg; // consistent

        ApplyThemeColorsToForm(this, bg, surface, surfaceAlt, text, border, accentActive, blossom);
        ApplySunnyUiControlColors(bg, surface, surfaceAlt, text, border, accentActive); // only applies deep changes for dark now
        if (dark)
        {
            // enforce palette immediately & schedule idle finalize
            ForceDarkControlPalette(surface, surfaceAlt, text, border, accentActive);
        }
        ApplyTitleBarColors(bg, surface, surfaceAlt, text, border, accentActive, dark);
        ApplyFixedTitleFont();
        listEntries?.Invalidate();

        // Re-schedule post-apply finalize per theme to capture late Sunny.UI paints
        Application.Idle -= ThemeIdleFinalize; // avoid duplicates
        Application.Idle += ThemeIdleFinalize;

        // Re-arm dark idle fix every time dark theme is applied
        if (dark)
        {
            _pendingDarkIdleFix = true;
            Application.Idle -= Application_Idle_DarkFix;
            Application.Idle += Application_Idle_DarkFix;
        }
    }

    private void ThemeIdleFinalize(object? sender, EventArgs e)
    {
        Application.Idle -= ThemeIdleFinalize;
        try
        {
            if (_settings.Theme == AppTheme.Dark)
            {
                // one more enforcement for dark palette to avoid drift
                var (bg, surface, surfaceAlt, text, border, accentActive, _) = GetTheme();
                ForceDarkControlPalette(surface, surfaceAlt, text, border, accentActive);
            }
            else
            {
                // ensure default accent colors restored (turn off custom mode again)
                ResetSunnyUiCustomMode(this);
            }
            listEntries?.Invalidate();
            txtSearch?.Invalidate();
        }
        catch { }
    }

    // Reset StyleCustomMode=false recursively for Sunny.UI controls so their internal style colors are used
    private void ResetSunnyUiCustomMode(Control root)
    {
        if (root == null) return;
        if (root.GetType().Namespace?.StartsWith("Sunny.UI") == true)
        {
            TrySetProp(root, "StyleCustomMode", false);
        }
        foreach (Control c in root.Controls) ResetSunnyUiCustomMode(c);
    }

    private Color BuildMultiSelectFill(Color accent, Color surface, Color text, bool dark)
    {
        // If accent too close to surface, shift it.
        double Contrast(Color a, Color b)
        {
            double Lum(Color c)
            {
                static double Srgb(double ch) { ch /= 255.0; return ch <= 0.03928 ? ch / 12.92 : Math.Pow((ch + 0.055)/1.055,2.4); }
                return 0.2126*Srgb(c.R)+0.7152*Srgb(c.G)+0.0722*Srgb(c.B);
            }
            double l1 = Lum(a)+0.05, l2 = Lum(b)+0.05; return l1>l2? l1/l2 : l2/l1;
        }
        var baseContrast = Contrast(accent, surface);
        Color adj = accent;
        if (baseContrast < 2.2)
        {
            // push away: if dark theme lighten, else darken
            if (dark)
            {
                int r = Math.Min(255, accent.R + 40);
                int g = Math.Min(255, accent.G + 40);
                int b = Math.Min(255, accent.B + 40);
                adj = Color.FromArgb(adjustAlpha(accent.A), r,g,b);
            }
            else
            {
                int r = Math.Max(0, accent.R - 50);
                int g = Math.Max(0, accent.G - 50);
                int b = Math.Max(0, accent.B - 50);
                adj = Color.FromArgb(adjustAlpha(accent.A), r,g,b);
            }
        }
        else adj = Color.FromArgb(adjustAlpha(accent.A), accent.R, accent.G, accent.B);
        return adj;

        int adjustAlpha(int a) => 220; // strong overlay
    }

    private Color ChooseBorder(Color fill, Color surface, Color text)
    {
        // simple contrast-based border (1px)
        int diff = Math.Abs(fill.R - surface.R) + Math.Abs(fill.G - surface.G) + Math.Abs(fill.B - surface.B);
        if (diff < 120)
        {
            // pick text color for border
            return text;
        }
        // subtle lighter/darker border
        int r = Math.Min(255, fill.R + (_isDarkTheme ? 30 : -30));
        int g = Math.Min(255, fill.G + (_isDarkTheme ? 30 : -30));
        int b = Math.Min(255, fill.B + (_isDarkTheme ? 30 : -30));
        r = Math.Max(0,r); g = Math.Max(0,g); b = Math.Max(0,b);
        return Color.FromArgb(255, r,g,b);
    }

    private void ApplyFixedTitleFont()
    {
        if (_titleFontAdjusted) return;
        try
        {
            var f = this.TitleFont; if (f == null) return;
            const float target = 10f; // enlarge title font
            if (Math.Abs(f.Size - target) > 0.1f)
            {
                this.TitleFont = new Font(f.FontFamily, target, f.Style, f.Unit);
            }
            this.TitleHeight = (int)Math.Ceiling(this.TitleFont.GetHeight() + 8);
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
        // For non-dark themes, rely on Sunny.UI built-in styling; only apply minimal text / watermark adjustments
        if (!dark)
        {
            if (txtSearch != null)
            {
                if (string.IsNullOrEmpty(txtSearch.Text)) txtSearch.Watermark = "Search...";
                txtSearch.Font = _uiFont;
                // Improve placeholder contrast for light / blossom themes
                try
                {
                    var (_, _, _, textColor, _, _, _) = GetTheme();
                    // semi-transparent version of text color (or fallback gray)
                    var wm = Color.FromArgb(140, textColor);
                    TrySetProp(txtSearch, "WatermarkColor", wm);
                }
                catch { }
            }
            if (lblStatus != null) lblStatus.Font = _uiFont;
            return; // skip deep color overrides so accent blues/reds remain intact
        }

        // Dark theme custom coloring below
        if (txtSearch != null)
        {
            txtSearch.StyleCustomMode = true;
            txtSearch.FillColor = surface; txtSearch.RectColor = border; txtSearch.ForeColor = text; if (string.IsNullOrEmpty(txtSearch.Text)) txtSearch.Watermark = "Search..."; txtSearch.Font = _uiFont; 
            TrySetProp(txtSearch, "FillColor2", surface);
            TrySetProp(txtSearch, "FillDisableColor", surface);
            TrySetProp(txtSearch, "FillReadOnlyColor", surface);
            TrySetProp(txtSearch, "RectDisableColor", border);
            TrySetProp(txtSearch, "RectReadOnlyColor", border);
            TrySetProp(txtSearch, "RectHoverColor", border);
            TrySetProp(txtSearch, "RectFocusColor", border);
            TrySetProp(txtSearch, "ForeDisableColor", text);
            TrySetProp(txtSearch, "ForeReadOnlyColor", text);
            TrySetProp(txtSearch, "FillHoverColor", surface);
            TrySetProp(txtSearch, "FillPressColor", surface);
            TrySetProp(txtSearch, "FillFocusColor", surface);
            TrySetProp(txtSearch, "FillSelectedColor", surface);
            txtSearch.ControlAdded -= TxtSearch_ControlAdded; // avoid duplicates
            txtSearch.ControlAdded += TxtSearch_ControlAdded;
            txtSearch.Enter -= TxtSearch_Enter; txtSearch.Enter += TxtSearch_Enter; // ensure reapply
            txtSearch.Leave -= TxtSearch_Leave; txtSearch.Leave += TxtSearch_Leave;
            txtSearch.MouseEnter -= TxtSearch_MouseEnter; txtSearch.MouseEnter += TxtSearch_MouseEnter;
            txtSearch.MouseLeave -= TxtSearch_MouseLeave; txtSearch.MouseLeave += TxtSearch_MouseLeave;
            FixSearchInnerColors();
            txtSearch.Invalidate();
            Application.Idle -= SearchIdleEnforce; // avoid duplicates
            Application.Idle += SearchIdleEnforce;
        }
        if (btnClearSearch != null)
        {
            btnClearSearch.StyleCustomMode = true;
            btnClearSearch.FillColor = surface; btnClearSearch.RectColor = surface; btnClearSearch.ForeColor = text; btnClearSearch.FillHoverColor = surfaceAlt; btnClearSearch.FillPressColor = accent; btnClearSearch.Font = _uiFont;
        }
        if (cmbKindFilter != null)
        {
            cmbKindFilter.StyleCustomMode = true;
            cmbKindFilter.FillColor = surface; cmbKindFilter.RectColor = border; cmbKindFilter.ForeColor = text; cmbKindFilter.DropDownStyle = Sunny.UI.UIDropDownStyle.DropDownList; cmbKindFilter.Font = _uiFont;
        }
        if (btnSettings != null)
        {
            btnSettings.StyleCustomMode = true;
            btnSettings.FillColor = surface; btnSettings.RectColor = surface; btnSettings.ForeColor = text; btnSettings.FillHoverColor = surfaceAlt; btnSettings.FillPressColor = accent; btnSettings.Font = _uiFont;
        }
        foreach (var b in new[] { btnAdd, btnCopy, btnImport, btnExport })
        {
            if (b == null) continue; b.StyleCustomMode = true; b.FillColor = surface; b.RectColor = border; b.ForeColor = text; b.FillHoverColor = surfaceAlt; b.FillPressColor = accent; b.RectHoverColor = border; b.RectPressColor = accent; b.Font = _uiFont;
        }
        if (listEntries != null)
        {
            listEntries.StyleCustomMode = true;
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
        // hook paint for multi-select overlay
        if (listEntries != null)
        {
            listEntries.Paint -= listEntries_Paint;
            listEntries.Paint += listEntries_Paint;
            cmsGrid.Opening += CmsGrid_Opening;
        }
    }

    private void LoadData()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_dataFile))
                _dataFile = Path.Combine(GetActiveDataDir(), "commands.json");
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

    private IEnumerable<CommandEntry> GetMultiSelectedEntries()
    {
        if (_multiSelected.Count == 0)
        {
            var single = GetSelected();
            if (single != null) yield return single;
            yield break;
        }
        var names = _multiSelected.Where(i => i >= 0 && i < listEntries.Items.Count).Select(i => listEntries.Items[i]?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _all) if (names.Contains(e.Name)) yield return e;
    }

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
    private void listEntries_KeyDown(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.C)) { CopySelected(); e.Handled = true; } else if (e.KeyCode == Keys.Delete) { btnDelete_Click(sender, EventArgs.Empty); e.Handled = true; } }
    private void listEntries_MouseMove(object sender, MouseEventArgs e)
    { 
        if (listEntries == null) return;
        int itemHeight = listEntries.ItemHeight; if (itemHeight <= 0) itemHeight = (int)_uiFont.GetHeight() + 6;
        int first = GetFirstVisibleIndex();
        int index = first + (e.Y / itemHeight);
        if (index < 0 || index >= listEntries.Items.Count) index = -1;
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            _lastToolTipTime = DateTime.MinValue;
        }
    }
    private void listEntries_MouseDown(object sender, MouseEventArgs e) { 
        if (listEntries == null) return; 
        int itemHeight = listEntries.ItemHeight; if (itemHeight <= 0) itemHeight = (int)_uiFont.GetHeight() + 6; 
        int first = GetFirstVisibleIndex();
        int index = first + (e.Y / itemHeight); if (index < 0 || index >= listEntries.Items.Count) index = -1; 
        if (e.Button == MouseButtons.Left && index >= 0)
        {
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            if (shift && _multiAnchor >= 0)
            {
                _multiSelected.Clear();
                int a = Math.Min(_multiAnchor, index); int b = Math.Max(_multiAnchor, index);
                for (int i = a; i <= b; i++) _multiSelected.Add(i);
            }
            else if (ctrl)
            {
                if (_multiSelected.Contains(index)) _multiSelected.Remove(index); else _multiSelected.Add(index);
                _multiAnchor = index;
            }
            else
            {
                _multiSelected.Clear();
                _multiSelected.Add(index);
                _multiAnchor = index;
            }
            listEntries.SelectedIndex = index; 
            listEntries.Invalidate();
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (!_multiSelected.Contains(index))
            {
                _multiSelected.Clear();
                if (index >= 0) { _multiSelected.Add(index); listEntries.SelectedIndex = index; _multiAnchor = index; }
                listEntries.Invalidate();
            }
        }
    }
    private void listEntries_MouseLeave(object? sender, EventArgs e) { _hoverIndex = -1; _listTooltip.Hide(listEntries); }
    private void ListTooltip_Popup(object? sender, PopupEventArgs e)
    {
        string tip = (sender as ToolTip)?.GetToolTip(listEntries) ?? string.Empty;
        var font = GetTooltipFont(tip);
        // allow wider / taller tooltips to avoid clipping
        var max = new Size(1000, 1400);
        var measureBounds = new Size(max.Width, max.Height);
        var sz = TextRenderer.MeasureText(tip, font, measureBounds, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        e.ToolTipSize = new Size(Math.Min(max.Width, sz.Width + 14), Math.Min(max.Height, sz.Height + 10));
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

    private void listEntries_Paint(object? sender, PaintEventArgs e)
    {
        if (listEntries == null || _multiSelected.Count <= 1) return; 
        try
        {
            int itemHeight = listEntries.ItemHeight; if (itemHeight <= 0) itemHeight = (int)_uiFont.GetHeight() + 6;
            int first = GetFirstVisibleIndex();
            using var selBrush = new SolidBrush(_multiSelectFill);
            using var borderPen = new Pen(_multiSelectBorder, 1f);
            foreach (var i in _multiSelected)
            {
                if (i < 0 || i >= listEntries.Items.Count) continue;
                int visibleRow = i - first; if (visibleRow < 0) continue;
                int y = visibleRow * itemHeight; if (y >= listEntries.Height) continue;
                var rect = new Rectangle(0, y, listEntries.Width - 1, itemHeight - 1);
                e.Graphics.FillRectangle(selBrush, rect);
                e.Graphics.DrawRectangle(borderPen, rect);
            }
        }
        catch { }
    }

    private void TryEnableDoubleBuffer(Control? c)
    {
        if (c == null) return;
        try
        {
            var prop = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            prop?.SetValue(c, true);
        }
        catch { }
    }

    // Restore tooltip font helper (CJK detection)
    private Font GetTooltipFont(string text)
    {
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

    private string? _activeTooltipText; // last shown tooltip text cache
    private int _activeTooltipIndex = -1; // last index tooltip was shown for
    private DateTime _lastTooltipShowTime = DateTime.MinValue; // last actual Show()
    private bool _lastShiftPressed = false; // track shift state to refresh encrypted tooltip

    private void TooltipTimer_Tick(object? sender, EventArgs e)
    {
        if (listEntries == null) return;
        bool shiftPressed = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
        bool shiftChanged = shiftPressed != _lastShiftPressed;
        if (_hoverIndex < 0 || _hoverIndex >= listEntries.Items.Count)
        {
            _listTooltip.Hide(listEntries); _activeTooltipIndex = -1; _activeTooltipText = null; _lastShiftPressed = shiftPressed; return;
        }
        // Only skip if same index, no shift change, and within cache interval
        if (_hoverIndex == _activeTooltipIndex && !shiftChanged && (DateTime.Now - _lastTooltipShowTime).TotalMilliseconds < 800)
            return;
        var name = listEntries.Items[_hoverIndex]?.ToString();
        if (string.IsNullOrEmpty(name)) { _lastShiftPressed = shiftPressed; return; }
        var entry = _all.FirstOrDefault(x => x.Name == name);
        if (entry == null) { _lastShiftPressed = shiftPressed; return; }
        string raw;
        if (entry.IsEncrypted)
        {
            try
            {
                if (shiftPressed)
                { raw = SecretProtector.Unprotect(entry.Value); }
                else
                {
                    var plain = SecretProtector.Unprotect(entry.Value);
                    raw = entry.Name + " (secret)\n" + new string('*', Math.Min(plain.Length, 6)) + "  (Hold Shift to reveal)";
                }
            }
            catch { raw = entry.Name + " (secret)"; }
        }
        else raw = entry.Value;
        string tip = WrapLongSegments(raw, 120);
        // Skip only if identical text, same index, no shift change and still within longer cache window
        if (!shiftChanged && _activeTooltipIndex == _hoverIndex && _activeTooltipText == tip && (DateTime.Now - _lastTooltipShowTime).TotalSeconds < 5)
        { _lastShiftPressed = shiftPressed; return; }
        var clientPos = listEntries.PointToClient(Cursor.Position);
        int duration = Math.Min(30000, Math.Max(6000, tip.Length * 40));
        _listTooltip.Show(tip, listEntries, Math.Min(clientPos.X + 24, listEntries.Width - 60), clientPos.Y + 24, duration);
        _activeTooltipIndex = _hoverIndex;
        _activeTooltipText = tip;
        _lastTooltipShowTime = DateTime.Now;
        _lastShiftPressed = shiftPressed;
    }

    private static string WrapLongSegments(string text, int maxRun)
    {
        if (string.IsNullOrEmpty(text) || maxRun <= 10) return text;
        var sb = new System.Text.StringBuilder(text.Length + 32);
        int run = 0;
        foreach (char c in text)
        {
            if (c == '\n' || c == '\r' || c == ' ' || c == '\t') run = 0; else run++;
            sb.Append(c);
            if (run >= maxRun) { sb.Append('\n'); run = 0; }
        }
        return sb.ToString();
    }

    private void cmbKindFilter_SelectedIndexChanged(object sender, EventArgs e) => ApplyFilter();
    private void txtSearch_TextChanged(object sender, EventArgs e)
    { ApplyFilter(); if (btnClearSearch != null) btnClearSearch.Visible = !string.IsNullOrEmpty(txtSearch.Text); }
    private void btnAdd_Click(object sender, EventArgs e)
    { using var form = new AddEditCommandForm(); form.Owner = this; if (form.ShowDialog(this) == DialogResult.OK && form.Entry != null) { form.Entry.CreatedUtc = form.Entry.UpdatedUtc = DateTime.UtcNow; _all.Add(form.Entry); SaveData(); ApplyFilter(); if (!string.IsNullOrWhiteSpace(form.Entry.Kind)) { _kinds.Add(form.Entry.Kind); RebuildKindFilterItems(); } } }
    private void btnEdit_Click(object sender, EventArgs e)
    { var sel = GetSelected(); if (sel == null) return; using var form = new AddEditCommandForm(sel); form.Owner = this; if (form.ShowDialog(this) == DialogResult.OK && form.Entry != null) { sel.Name = form.Entry.Name; sel.Value = form.Entry.Value; sel.Description = form.Entry.Description; sel.Kind = form.Entry.Kind; sel.UpdatedUtc = DateTime.UtcNow; SaveData(); ApplyFilter(); if (!string.IsNullOrWhiteSpace(sel.Kind)) { _kinds.Add(sel.Kind); RebuildKindFilterItems(); } } }
    private void btnDelete_Click(object sender, EventArgs e)
    { 
        var multi = GetMultiSelectedEntries().ToList();
        if (multi.Count == 0) { var sel = GetSelected(); if (sel == null) return; multi.Add(sel); }
        if (multi.Count == 0) return;
        string prompt = multi.Count == 1 ? $"Delete this item: {multi[0].Name}?" : $"Delete these {multi.Count} items?";
        if (MessageBox.Show(prompt, "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            foreach (var item in multi) _all.Remove(item);
            _multiSelected.Clear();
            SaveData(); ApplyFilter();
        }
    }
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
    { using var dlg = new SettingsForm(_settings); dlg.Owner = this; if (dlg.ShowDialog(this) == DialogResult.OK) { float prevSize = _uiFont.Size; _settings.Save(); _dataFile = Path.Combine(GetActiveDataDir(), "commands.json");
            if (Math.Abs(prevSize - _settings.UiFontSize) > 0.1f)
            {
                try { var nf = CreateUiFont(_settings.UiFontSize); var old = _uiFont; _uiFont = nf; old.Dispose(); } catch { }
                ApplyGlobalFont(this.Controls);
            }
            LoadData(); ApplyFilter(); ApplyTheme(); if (_trayIcon != null && this.Icon != null) _trayIcon.Icon = this.Icon; } }

    private void InitTray()
    {
        _trayIcon = new NotifyIcon { Icon = this.Icon ?? SystemIcons.Application, Text = "CmdKit (Ctrl+Q)", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _trayIcon.ContextMenuStrip.Opening += CmsGrid_Opening;
        _trayIcon.ContextMenuStrip.Items.Add("Show", null, (s, e) => ShowAndActivate());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e) => { _allowExit = true; UnregisterAndDispose(); Application.Exit(); });
        _trayIcon.DoubleClick += (s, e) => ShowAndActivate();
    }

    private void CmsGrid_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // update context menu item text for delete when multi selected in main list context menu
        try
        {
            if (cmsGrid != null && miDelete != null)
            {
                int count = _multiSelected.Count == 0 ? (GetSelected() != null ? 1 : 0) : _multiSelected.Count;
                miDelete.Text = count > 1 ? $"Delete ({count})" : "Delete";
            }
        }
        catch { }
    }

    private void TryRegisterHotKey() { /* no-op: handled by GlobalHotkeyHost */ }
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
    {
        // only handle custom sizing; global hotkey handled by GlobalHotkeyHost hidden window
        if (m.Msg == WM_NCHITTEST && this.FormBorderStyle == FormBorderStyle.Sizable)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                const int grip = 6;
                Point p = PointToClient(new Point((int)m.LParam & 0xFFFF, (int)m.LParam >> 16));
                bool left = p.X <= grip;
                bool right = p.X >= Width - grip;
                bool top = p.Y <= grip;
                bool bottom = p.Y >= Height - grip;
                if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
            }
            return;
        }
        base.WndProc(ref m);
    }

    private void CmdKitForm_FormClosing(object? sender, FormClosingEventArgs e) { if (!_allowExit) { e.Cancel = true; this.Hide(); } else { UnregisterAndDispose(); } }
    private void UnregisterAndDispose() { try { _hotkeyHost?.Dispose(); } catch { } if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; } }

    #region SettingsForm
    private class SettingsForm : Form
    {
        private readonly AppSettings _settings; private TextBox txtPath = new(); private Button btnBrowse = new(); private ComboBox cmbTheme = new(); private Button btnOk = new(); private Button btnCancel = new(); private CheckBox chkAutoClose = new(); private NumericUpDown nudFont = new();
        public SettingsForm(AppSettings settings)
        {
            _settings = settings; Text = "Settings"; Width = 520; Height = 360; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            int left = 24; int top = 24; int labelW = 140; int gap = 40; int inputLeft = left + labelW + 8; int inputW = 260;
            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CmdKit");
            string initialPath = string.IsNullOrWhiteSpace(_settings.DataPath) ? defaultPath : _settings.DataPath;
            Controls.Add(MakeLabel("Data Folder", left, top)); txtPath.SetBounds(inputLeft, top - 2, inputW, 26); txtPath.Text = initialPath; Controls.Add(txtPath); btnBrowse.Text = "..."; btnBrowse.SetBounds(inputLeft + inputW + 6, top - 2, 34, 26); btnBrowse.Click += Browse; btnBrowse.FlatStyle = FlatStyle.Flat; btnBrowse.FlatAppearance.BorderSize = 1; Controls.Add(btnBrowse); top += gap;
            Controls.Add(MakeLabel("Theme", left, top)); cmbTheme.SetBounds(inputLeft, top - 2, 180, 26); cmbTheme.DropDownStyle = ComboBoxStyle.DropDownList; cmbTheme.Items.AddRange(new object[] { AppTheme.Dark.ToString(), AppTheme.Light.ToString(), AppTheme.Blossom.ToString() }); cmbTheme.SelectedItem = _settings.Theme.ToString(); Controls.Add(cmbTheme); top += gap;
            Controls.Add(MakeLabel("Font Size", left, top)); nudFont.SetBounds(inputLeft, top - 2, 100, 26); nudFont.Minimum = 8; nudFont.Maximum = 24; nudFont.DecimalPlaces = 1; nudFont.Increment = 0.5M; nudFont.Value = (decimal)Math.Max(8, Math.Min(24, _settings.UiFontSize)); Controls.Add(nudFont); top += gap;
            chkAutoClose.Text = "Auto close after copy"; chkAutoClose.SetBounds(inputLeft, top - 6, 220, 28); chkAutoClose.Checked = _settings.AutoCloseAfterCopy; Controls.Add(chkAutoClose); top += gap;
            btnOk.Text = "OK"; btnOk.SetBounds(inputLeft + 60, top, 90, 34); btnOk.Click += (s, e) => { Apply(); DialogResult = DialogResult.OK; };
            btnCancel.Text = "Cancel"; btnCancel.SetBounds(inputLeft + 160, top, 90, 34); btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel; Controls.Add(btnOk); Controls.Add(btnCancel);
        }
        private void Browse(object? sender, EventArgs e) { using var f = new FolderBrowserDialog(); if (f.ShowDialog(this) == DialogResult.OK) txtPath.Text = f.SelectedPath; }
        private void Apply() { _settings.DataPath = txtPath.Text.Trim(); if (Enum.TryParse<AppTheme>(cmbTheme.SelectedItem?.ToString(), out var theme)) _settings.Theme = theme; _settings.AutoCloseAfterCopy = chkAutoClose.Checked; _settings.UiFontSize = (float)nudFont.Value; }
        private static Label MakeLabel(string text, int left, int top) => new() { Text = text, Left = left, Top = top, Width = 140, TextAlign = ContentAlignment.MiddleRight };
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
        private readonly Button btnOk = new();
        private readonly Button btnCancel = new();
        public AddEditCommandForm(CommandEntry? existing = null)
        {
            Entry = existing == null ? new CommandEntry() : new CommandEntry { Id = existing.Id, Name = existing.Name, Value = existing.Value, Description = existing.Description, Kind = existing.Kind, CreatedUtc = existing.CreatedUtc, UpdatedUtc = existing.UpdatedUtc };
            Text = existing == null ? "Add Entry" : "Edit Entry"; Width = 480; Height = 420; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            txtValue.Multiline = true; txtValue.Height = 70; txtValue.ScrollBars = ScrollBars.Vertical; txtDesc.Multiline = true; txtDesc.Height = 70; txtDesc.ScrollBars = ScrollBars.Vertical;
            int left = 20; int top = 20; int lblw = 85; int gap = 32; int inputLeft = left + lblw + 6; int inputW = 330;
            Controls.Add(MakeLabel("Name", left, top)); txtName.SetBounds(inputLeft, top - 2, inputW, 23); txtName.Text = Entry!.Name; Controls.Add(txtName); top += gap;
            Controls.Add(MakeLabel("Value", left, top)); txtValue.SetBounds(inputLeft, top - 2, inputW, 70); Controls.Add(txtValue); top += 78;
            Controls.Add(MakeLabel("Description", left, top)); txtDesc.SetBounds(inputLeft, top - 2, inputW, 70); txtDesc.Text = Entry.Description; Controls.Add(txtDesc); top += 78;
            Controls.Add(MakeLabel("Type", left, top)); cmbKind.SetBounds(inputLeft, top - 2, 200, 23); cmbKind.DropDownStyle = ComboBoxStyle.DropDown; cmbKind.AutoCompleteMode = AutoCompleteMode.SuggestAppend; cmbKind.AutoCompleteSource = AutoCompleteSource.CustomSource; Controls.Add(cmbKind); top += gap;
            chkSecret.Text = "Encrypt"; chkSecret.SetBounds(inputLeft, top - 4, 80, 24); chkSecret.Checked = Entry.IsEncrypted || ((CmdKitForm?)Owner)?.ShouldEncrypt(Entry!) == true; Controls.Add(chkSecret); top += gap;
            btnOk.Text = "OK"; btnOk.SetBounds(inputLeft + 60, top, 90, 32); btnOk.Click += (s, e) => OnOk();
            btnCancel.Text = "Cancel"; btnCancel.SetBounds(inputLeft + 160, top, 90, 32); btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel; Controls.AddRange(new Control[] { btnOk, btnCancel });
        }
        private static Label MakeLabel(string t, int l, int tp) => new() { Text = t, Left = l, Top = tp, Width = 85, TextAlign = ContentAlignment.MiddleRight };
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
            try { _hotkeyHost?.Dispose(); } catch { }
            _filterCts?.Cancel(); _filterCts?.Dispose(); _listTooltip?.Dispose(); _trayIcon?.Dispose(); _tooltipTimer?.Stop(); _tooltipTimer?.Dispose();
            _searchEnforceTimer?.Stop(); _searchEnforceTimer?.Dispose();
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
    private void TxtSearch_ControlAdded(object? sender, ControlEventArgs e) => FixSearchInnerColors();
    private void TxtSearch_Enter(object? sender, EventArgs e) => FixSearchInnerColors();
    private void TxtSearch_Leave(object? sender, EventArgs e) => FixSearchInnerColors();
    private void SearchIdleEnforce(object? sender, EventArgs e) { Application.Idle -= SearchIdleEnforce; FixSearchInnerColors(); txtSearch?.Invalidate(); }
    private void TxtSearch_MouseEnter(object? sender, EventArgs e)
    {
        if (_settings.Theme == AppTheme.Dark)
        {
            ForceSearchColorsDark();
            if (!_searchEnforceTimer.Enabled) _searchEnforceTimer.Start();
        }
    }
    private void TxtSearch_MouseLeave(object? sender, EventArgs e)
    {
        if (_settings.Theme == AppTheme.Dark && txtSearch != null && !txtSearch.Focused)
        {
            Task.Delay(200).ContinueWith(_ => { try { if (txtSearch != null && !txtSearch.IsDisposed && !txtSearch.Focused) _searchEnforceTimer?.Stop(); } catch { } });
        }
    }

    // Determine first visible item index in listEntries (Sunny.UI.UIListBox) using multiple fallbacks
    private int GetFirstVisibleIndex()
    {
        if (listEntries == null || listEntries.Items.Count == 0) return 0;
        int top = 0;
        try
        {
            // 1. Try public/reflective TopIndex property (standard ListBox style)
            var p = listEntries.GetType().GetProperty("TopIndex", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(int))
            {
                top = (int)p.GetValue(listEntries)!;
                return ClampTop(top);
            }
        }
        catch { }
        try
        {
            // 2. Try internal field names often used
            string[] fieldCandidates = { "topIndex", "_topIndex", "firstVisible", "_firstVisible", "startIndex" };
            foreach (var fName in fieldCandidates)
            {
                var f = listEntries.GetType().GetField(fName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(int))
                {
                    top = (int)f.GetValue(listEntries)!;
                    return ClampTop(top);
                }
            }
        }
        catch { }
        try
        {
            // 3. Derive from embedded vScrollBar
            var vsbField = listEntries.GetType().GetField("vScrollBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (vsbField?.GetValue(listEntries) is ScrollBar vsb)
            {
                int itemHeight = listEntries.ItemHeight; if (itemHeight <= 0) itemHeight = (int)_uiFont.GetHeight() + 6;
                // If Value increments by 1 per item, treat directly; if large, divide by itemHeight
                if (vsb.LargeChange <= listEntries.Items.Count + 1 && vsb.Maximum <= listEntries.Items.Count + vsb.LargeChange + 2)
                {
                    top = vsb.Value; // per-item mode
                }
                else
                {
                    top = vsb.Value / Math.Max(1, itemHeight); // pixel mode -> per-item
                }
                return ClampTop(top);
            }
        }
        catch { }
        return 0;

        int ClampTop(int t) => t < 0 ? 0 : (t >= listEntries.Items.Count ? listEntries.Items.Count - 1 : t);
    }

    private (int scrollVal, bool pixelBased) GetListScrollOffset()
    {
        // Deprecated: replaced by GetFirstVisibleIndex for accurate hit testing
        return (0, true);
    }

    private class GlobalHotkeyHost : NativeWindow, IDisposable
    {
        private readonly CmdKitForm _owner;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0x1100;
        private const int MOD_CONTROL = 0x0002;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public GlobalHotkeyHost(CmdKitForm owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams()); // message-only like hidden window
            Register();
        }
        private void Register()
        {
            try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }
            try { RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL, (int)Keys.Q); } catch { }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                try { _owner.BeginInvoke(new Action(() => _owner.ToggleVisibility())); } catch { }
            }
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }
            try { DestroyHandle(); } catch { }
        }
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_SHOW = 5;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
}
