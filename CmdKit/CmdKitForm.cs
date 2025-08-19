using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Threading;
using CmdKit.Models;
using CmdKit.Settings;
using CmdKit.Theme;
using System.Collections.Specialized;
using CmdKit; // for SecretProtector
using System.Text.RegularExpressions;

namespace CmdKit;

public partial class CmdKitForm : Form
{
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
    private readonly HashSet<string> _kinds = new(StringComparer.OrdinalIgnoreCase) { "Command", "Link" };

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public CmdKitForm()
    {
        InitializeComponent();
        // Load icon (search output root then Resources folder)
        var icoPath = Path.Combine(AppContext.BaseDirectory, "CmdKit.ico");
        if (!File.Exists(icoPath))
        {
            var subPath = Path.Combine(AppContext.BaseDirectory, "Resources", "CmdKit.ico");
            if (File.Exists(subPath)) icoPath = subPath;
        }
        if (File.Exists(icoPath))
        {
            try { this.Icon = new Icon(icoPath); } catch { }
        }
        this.ShowIcon = true;
        _dataFile = Path.Combine(GetActiveDataDir(), "commands.json");
        ApplyTheme();
        InitTray();
        if (_trayIcon != null && this.Icon != null) _trayIcon.Icon = this.Icon;
        this.HandleCreated += (s, e) => TryRegisterHotKey();
        this.FormClosing += CmdKitForm_FormClosing;
    }

    private string GetActiveDataDir()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DataPath)) return _settings.DataPath;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CmdKit");
    }

    private void ApplyTheme()
    {
        Color bg, surface, surfaceAlt, textPrimary, border, accentActive;
        bool blossom = _settings.Theme == AppTheme.Blossom;
        bool light = _settings.Theme == AppTheme.Light;
        if (blossom)
        {
            bg = ThemeColorsBlossom.Background;
            surface = ThemeColorsBlossom.Surface;
            surfaceAlt = ThemeColorsBlossom.SurfaceAlt;
            textPrimary = ThemeColorsBlossom.TextPrimary;
            border = ThemeColorsBlossom.Border;
            accentActive = ThemeColorsBlossom.AccentActive;
        }
        else if (light)
        {
            bg = ThemeColorsLight.Background;
            surface = ThemeColorsLight.Surface;
            surfaceAlt = ThemeColorsLight.SurfaceAlt;
            textPrimary = ThemeColorsLight.TextPrimary;
            border = ThemeColorsLight.Border;
            accentActive = ThemeColorsLight.AccentActive;
        }
        else
        {
            bg = ThemeColors.Background;
            surface = ThemeColors.Surface;
            surfaceAlt = ThemeColors.SurfaceAlt;
            textPrimary = ThemeColors.TextPrimary;
            border = ThemeColors.Border;
            accentActive = ThemeColors.AccentActive;
        }
        this.BackColor = bg; this.ForeColor = textPrimary;
        foreach (Control c in Controls) StyleControlRecursiveTheme(c, bg, surface, surfaceAlt, textPrimary, border, accentActive);
        if (flowButtons != null) flowButtons.BackColor = bg;
        statusStrip.BackColor = surface; lblStatus.ForeColor = textPrimary;
        listEntries.BackColor = surface; listEntries.ForeColor = textPrimary;
        // Optional blossom flourish: light gradient background
        if (blossom)
        {
            this.Paint -= CmdKitForm_PaintBlossom;
            this.Paint += CmdKitForm_PaintBlossom;
            this.Invalidate();
        }
        else
        {
            this.Paint -= CmdKitForm_PaintBlossom;
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
        catch (Exception ex)
        {
            MessageBox.Show("Load data failed: " + ex.Message);
        }

        _kinds.Clear();
        foreach(var k in _all.Select(a=>a.Kind).Where(k=>!string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase)) _kinds.Add(k!);
        RebuildKindFilterItems();
    }

    private void SaveData()
    {
        try
        {
            if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
            // ensure encryption state
            foreach (var e in _all)
            {
                if (!e.IsEncrypted && e is { } && ShouldEncrypt(e) && !string.IsNullOrEmpty(e.Value))
                {
                    e.Value = SecretProtector.Protect(e.Value);
                }
            }
            string json = JsonSerializer.Serialize(_all, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_dataFile, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save data failed: " + ex.Message);
        }
    }

    private void RebuildKindFilterItems()
    {
        if (cmbKindFilter == null) return;
        var prev = cmbKindFilter.SelectedItem?.ToString();
        cmbKindFilter.BeginUpdate();
        cmbKindFilter.Items.Clear();
        cmbKindFilter.Items.Add("All");
        foreach (var k in _kinds.OrderBy(x=>x)) cmbKindFilter.Items.Add(k);
        cmbKindFilter.EndUpdate();
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
                await Task.Delay(180, cts.Token); // debounce
                if (cts.IsCancellationRequested) return;
                var snapshot = _all.ToArray();
                string kw = string.Empty;
                Invoke(new Action(() => kw = txtSearch.Text?.Trim() ?? string.Empty));
                int kindIndex = -1; string kindText = string.Empty;
                Invoke(new Action(() => { if (cmbKindFilter != null) { kindIndex = cmbKindFilter.SelectedIndex; kindText = cmbKindFilter.SelectedItem?.ToString() ?? ""; } }));
                IEnumerable<CommandEntry> data = snapshot;
                if (kindIndex > 0 && !string.IsNullOrEmpty(kindText))
                {
                    data = data.Where(x => string.Equals(x.Kind, kindText, StringComparison.OrdinalIgnoreCase));
                }
                if (!string.IsNullOrEmpty(kw))
                    data = data.Where(x => x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) || (x.Description ?? string.Empty).Contains(kw, StringComparison.OrdinalIgnoreCase));
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
            catch (Exception ex)
            {
                try { Invoke(new Action(() => UpdateStatus("Filter error: " + ex.Message))); } catch { }
            }
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
        catch (Exception ex)
        {
            MessageBox.Show("Copy failed: " + ex.Message);
            UpdateStatus("Copy failed.");
        }
    }

    private void listEntries_DoubleClick(object sender, EventArgs e) => CopySelected();
    private void listEntries_KeyDown(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.C)) { CopySelected(); e.Handled = true; } }
    private void listEntries_MouseMove(object sender, MouseEventArgs e)
    {
        int index = listEntries.IndexFromPoint(e.Location);
        if (index >= 0 && index < listEntries.Items.Count)
        {
            var name = listEntries.Items[index].ToString();
            var entry = _all.FirstOrDefault(x => x.Name == name);
            if (entry != null && (DateTime.Now - _lastToolTipTime).TotalMilliseconds > 300)
            {
                string tip;
                if (entry.IsEncrypted)
                {
                    if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                    {
                        tip = SecretProtector.Unprotect(entry.Value);
                    }
                    else
                    {
                        var plain = SecretProtector.Unprotect(entry.Value);
                        tip = entry.Name + " (secret)\n" + new string('*', Math.Min(plain.Length, 6)) + "  (Hold Shift to view)";
                    }
                }
                else tip = entry.Value;
                _listTooltip.SetToolTip(listEntries, tip);
                _lastToolTipTime = DateTime.Now;
            }
        }
    }
    private void listEntries_MouseDown(object sender, MouseEventArgs e)
    { if (e.Button == MouseButtons.Right) { int index = listEntries.IndexFromPoint(e.Location); if (index >= 0 && index < listEntries.Items.Count) listEntries.SelectedIndex = index; } }

    private void cmbKindFilter_SelectedIndexChanged(object sender, EventArgs e) => ApplyFilter();
    private void txtSearch_TextChanged(object sender, EventArgs e)
    {
        ApplyFilter();
        if (btnClearSearch != null) btnClearSearch.Visible = !string.IsNullOrEmpty(txtSearch.Text);
    }
    private void btnAdd_Click(object sender, EventArgs e)
    { var form = new AddEditCommandForm(); if (form.ShowDialog(this) == DialogResult.OK && form.Entry != null) { form.Entry.CreatedUtc = form.Entry.UpdatedUtc = DateTime.UtcNow; _all.Add(form.Entry); SaveData(); ApplyFilter(); if(!string.IsNullOrWhiteSpace(form.Entry.Kind)) { _kinds.Add(form.Entry.Kind); RebuildKindFilterItems(); } } }
    private void btnEdit_Click(object sender, EventArgs e)
    { var sel = GetSelected(); if (sel == null) return; var form = new AddEditCommandForm(sel); if (form.ShowDialog(this) == DialogResult.OK && form.Entry != null) { sel.Name = form.Entry.Name; sel.Value = form.Entry.Value; sel.Description = form.Entry.Description; sel.Kind = form.Entry.Kind; sel.UpdatedUtc = DateTime.UtcNow; SaveData(); ApplyFilter(); if(!string.IsNullOrWhiteSpace(sel.Kind)) { _kinds.Add(sel.Kind); RebuildKindFilterItems(); } } }
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
                foreach (var item in list)
                {
                    var exist = _all.FirstOrDefault(x => x.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
                    if (exist == null) _all.Add(item);
                    else
                    {
                        exist.Value = item.Value;
                        exist.Description = item.Description;
                        exist.Kind = item.Kind;
                        exist.UpdatedUtc = DateTime.UtcNow;
                    }
                    if(!string.IsNullOrWhiteSpace(item.Kind)) _kinds.Add(item.Kind);
                }
                foreach(var entry in _all) if (!entry.IsEncrypted && ShouldEncrypt(entry)) entry.Value = SecretProtector.Protect(entry.Value);
                RebuildKindFilterItems();
                SaveData(); ApplyFilter(); MessageBox.Show("Import completed");
            }
            catch (Exception ex) { MessageBox.Show("Import failed: " + ex.Message); }
        }
    }
    private void btnExport_Click(object sender, EventArgs e)
    { using var sfd = new SaveFileDialog { Filter = "JSON Files (*.json)|*.json", FileName = "commands_export.json" }; if (sfd.ShowDialog(this) == DialogResult.OK) { try { string json = JsonSerializer.Serialize(_all, new JsonSerializerOptions { WriteIndented = true }); File.WriteAllText(sfd.FileName, json); MessageBox.Show("Export completed"); } catch (Exception ex) { MessageBox.Show("Export failed: " + ex.Message); } } }
    private void btnReload_Click(object sender, EventArgs e) { /* removed refresh button */ }

    private void UpdateStatus(string text) { this.Text = "CmdKit - " + text; if (lblStatus != null) lblStatus.Text = text; }
    private void btnSettings_Click(object sender, EventArgs e)
    { using var dlg = new SettingsForm(_settings); if (dlg.ShowDialog(this) == DialogResult.OK) { _settings.Save(); _dataFile = Path.Combine(GetActiveDataDir(), "commands.json"); LoadData(); ApplyFilter(); ApplyTheme(); if (_trayIcon != null && this.Icon != null) _trayIcon.Icon = this.Icon; } }

    private void InitTray()
    {
        _trayIcon = new NotifyIcon { Icon = this.Icon ?? SystemIcons.Application, Text = "CmdKit (Ctrl+Q)", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _trayIcon.ContextMenuStrip.Items.Add("Show", null, (s, e) => ShowAndActivate());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e) => { _allowExit = true; UnregisterAndDispose(); Application.Exit(); });
        _trayIcon.DoubleClick += (s, e) => ShowAndActivate();
    }
    private void TryRegisterHotKey()
    { try { UnregisterHotKey(this.Handle, HOTKEY_ID); RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL, (int)Keys.Q); } catch (Exception ex) { UpdateStatus("Hotkey error: " + ex.Message); } }
    private void ShowAndActivate() { if (this.WindowState == FormWindowState.Minimized) this.WindowState = FormWindowState.Normal; if (!this.Visible) { ApplyTheme(); this.Show(); } else ApplyTheme(); this.Activate(); txtSearch.Focus(); }
    private void ToggleVisibility() { if (this.Visible && this.WindowState != FormWindowState.Minimized) this.Hide(); else ShowAndActivate(); }
    protected override void WndProc(ref Message m) { if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID) { ToggleVisibility(); return; } base.WndProc(ref m); }
    private void CmdKitForm_FormClosing(object? sender, FormClosingEventArgs e) { if (!_allowExit) { e.Cancel = true; this.Hide(); } else { UnregisterAndDispose(); } }
    private void UnregisterAndDispose() { try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { } if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; } }

    private class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private TextBox txtPath = new();
        private Button btnBrowse = new();
        private ComboBox cmbTheme = new();
        private Button btnOk = new();
        private Button btnCancel = new();
        private CheckBox chkAutoClose = new();
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
    }

    private bool MatchesSensitive(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (_settings.SensitivePatterns == null || _settings.SensitivePatterns.Count == 0) return false;
        foreach (var p in _settings.SensitivePatterns)
        {
            try { if (Regex.IsMatch(text, p, RegexOptions.IgnoreCase)) return true; } catch { }
        }
        return false;
    }
    private bool ShouldEncrypt(CommandEntry e)
    {
        if (MatchesSensitive(e.Name) || MatchesSensitive(e.Kind) || MatchesSensitive(e.Description ?? string.Empty)) return true;
        return false;
    }

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
            int left = 20; int top = 20; int lblw = 85; int gap = 32; int inputLeft = left + lblw + 6; int inputW = 300;
            Controls.Add(MakeLabel("Name", left, top)); txtName.SetBounds(inputLeft, top - 2, inputW, 23); txtName.Text = Entry!.Name; Controls.Add(txtName); top += gap;
            Controls.Add(MakeLabel("Value", left, top)); txtValue.SetBounds(inputLeft, top - 2, inputW - 34, 70); txtValue.Text = Entry.IsEncrypted ? SecretProtector.Unprotect(Entry.Value) : Entry.Value; Controls.Add(txtValue);
            btnToggleView.Text = "??"; btnToggleView.SetBounds(inputLeft + inputW - 32, top - 2, 30, 30); btnToggleView.Click += (s, e) => ToggleView(); Controls.Add(btnToggleView); top += 78;
            Controls.Add(MakeLabel("Description", left, top)); txtDesc.SetBounds(inputLeft, top - 2, inputW, 70); txtDesc.Text = Entry.Description; Controls.Add(txtDesc); top += 78;
            Controls.Add(MakeLabel("Type", left, top)); cmbKind.SetBounds(inputLeft, top - 2, 200, 23); cmbKind.DropDownStyle = ComboBoxStyle.DropDown; cmbKind.AutoCompleteMode = AutoCompleteMode.SuggestAppend; cmbKind.AutoCompleteSource = AutoCompleteSource.CustomSource; var ac = new AutoCompleteStringCollection(); ac.AddRange(((CmdKitForm)Owner ?? this.Owner as CmdKitForm)?._kinds.ToArray() ?? Array.Empty<string>()); cmbKind.AutoCompleteCustomSource = ac; cmbKind.Items.AddRange((((CmdKitForm)Owner)?._kinds.ToArray()) ?? Array.Empty<string>()); cmbKind.Text = Entry.Kind; Controls.Add(cmbKind); top += gap;
            chkSecret.Text = "Encrypt"; chkSecret.SetBounds(inputLeft, top - 4, 80, 24); chkSecret.Checked = Entry.IsEncrypted || ((CmdKitForm?)Owner)?.ShouldEncrypt(Entry!) == true; Controls.Add(chkSecret); top += gap;
            btnOk.Text = "OK"; btnOk.SetBounds(inputLeft + 60, top, 90, 32); btnOk.Click += (s, e) => OnOk();
            btnCancel.Text = "Cancel"; btnCancel.SetBounds(inputLeft + 160, top, 90, 32); btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel; Controls.AddRange(new Control[] { btnOk, btnCancel });
        }
        private static Label MakeLabel(string t, int l, int tp) => new() { Text = t, Left = l, Top = tp, Width = 85, TextAlign = ContentAlignment.MiddleRight };
        private void ToggleView()
        {
            valueVisible = !valueVisible;
            if (valueVisible)
            {
                txtValue.PasswordChar = '\0';
            }
            else
            {
                if (txtValue.PasswordChar == '\0') txtValue.PasswordChar = '•';
            }
        }
        private void OnOk()
        {
            if (string.IsNullOrWhiteSpace(txtName.Text) || string.IsNullOrWhiteSpace(txtValue.Text)) { MessageBox.Show("Name and Value are required."); return; }
            Entry!.Name = txtName.Text.Trim();
            var plain = txtValue.Text.Trim();
            Entry.Description = string.IsNullOrWhiteSpace(txtDesc.Text) ? null : txtDesc.Text.Trim();
            Entry.Kind = string.IsNullOrWhiteSpace(cmbKind.Text)?"Command":cmbKind.Text.Trim();
            if (chkSecret.Checked || ((CmdKitForm?)Owner)?.ShouldEncrypt(Entry) == true) Entry.Value = SecretProtector.Protect(plain); else Entry.Value = plain;
            Entry.UpdatedUtc = DateTime.UtcNow; DialogResult = DialogResult.OK;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _filterCts?.Cancel();
            _filterCts?.Dispose();
            _listTooltip?.Dispose();
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
