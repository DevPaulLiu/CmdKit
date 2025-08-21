namespace CmdKit
{
    partial class CmdKitForm
    {
        private Sunny.UI.UITextBox txtSearch;
        private Sunny.UI.UIButton btnClearSearch; // clear search button
        private Sunny.UI.UIListBox listEntries;
        private Sunny.UI.UIButton btnAdd;
        private Sunny.UI.UIButton btnCopy;
        private Sunny.UI.UIButton btnImport;
        private Sunny.UI.UIButton btnExport;
        private Sunny.UI.UIButton btnSettings;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ContextMenuStrip cmsGrid;
        private System.Windows.Forms.ToolStripMenuItem miCopy;
        private System.Windows.Forms.ToolStripMenuItem miEdit;
        private System.Windows.Forms.ToolStripMenuItem miDelete;
        private Sunny.UI.UIComboBox cmbKindFilter;
        private System.Windows.Forms.FlowLayoutPanel flowButtons;

        private System.ComponentModel.IContainer components = null;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.txtSearch = new Sunny.UI.UITextBox();
            this.btnClearSearch = new Sunny.UI.UIButton();
            this.listEntries = new Sunny.UI.UIListBox();
            this.btnAdd = new Sunny.UI.UIButton();
            this.btnCopy = new Sunny.UI.UIButton();
            this.btnImport = new Sunny.UI.UIButton();
            this.btnExport = new Sunny.UI.UIButton();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.cmsGrid = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.miCopy = new System.Windows.Forms.ToolStripMenuItem();
            this.miEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.miDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.cmbKindFilter = new Sunny.UI.UIComboBox();
            this.flowButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btnSettings = new Sunny.UI.UIButton();
            this.statusStrip.SuspendLayout();
            this.cmsGrid.SuspendLayout();
            this.flowButtons.SuspendLayout();
            this.SuspendLayout();
            // palette base (will be overridden by ApplyTheme)
            var bg = CmdKit.Theme.ThemeColors.Background;
            var surface = CmdKit.Theme.ThemeColors.Surface;
            var surfaceAlt = CmdKit.Theme.ThemeColors.SurfaceAlt;
            var text = CmdKit.Theme.ThemeColors.TextPrimary;
            var border = CmdKit.Theme.ThemeColors.Border;
            var accentActive = CmdKit.Theme.ThemeColors.AccentActive;
            this.BackColor = bg; this.ForeColor = text;
            int topH = 30; // unified top bar height
            // txtSearch
            this.txtSearch.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.txtSearch.RectColor = border; this.txtSearch.FillColor = surface; this.txtSearch.ForeColor = text;
            this.txtSearch.Location = new System.Drawing.Point(12, 12);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(210, topH);
            this.txtSearch.TabIndex = 0;
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            // clear button
            this.btnClearSearch.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClearSearch.FillColor = surface; this.btnClearSearch.RectColor = surface; this.btnClearSearch.ForeColor = text;
            this.btnClearSearch.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
            this.btnClearSearch.Location = new System.Drawing.Point(228, 12);
            this.btnClearSearch.Name = "btnClearSearch";
            this.btnClearSearch.Size = new System.Drawing.Size(28, topH);
            this.btnClearSearch.TabIndex = 1;
            this.btnClearSearch.Text = "×"; // multiply sign
            this.btnClearSearch.Visible = false;
            this.btnClearSearch.Click += (s, e) => { this.txtSearch.Text = string.Empty; this.txtSearch.Focus(); };
            // kind filter
            this.cmbKindFilter.DropDownStyle = Sunny.UI.UIDropDownStyle.DropDownList;
            this.cmbKindFilter.FillColor = surface; this.cmbKindFilter.RectColor = border; this.cmbKindFilter.ForeColor = text;
            this.cmbKindFilter.Items.AddRange(new object[] { "All" });
            this.cmbKindFilter.SelectedIndex = 0;
            this.cmbKindFilter.Location = new System.Drawing.Point(260, 12);
            this.cmbKindFilter.Name = "cmbKindFilter";
            this.cmbKindFilter.Size = new System.Drawing.Size(84, topH);
            this.cmbKindFilter.TabIndex = 2;
            this.cmbKindFilter.SelectedIndexChanged += new System.EventHandler(this.cmbKindFilter_SelectedIndexChanged);
            // settings button (use text icon)
            this.btnSettings.Text = "⚙"; // fallback gear char
            this.btnSettings.FillColor = surface; this.btnSettings.RectColor = surface; this.btnSettings.ForeColor = text;
            this.btnSettings.Location = new System.Drawing.Point(350, 12);
            this.btnSettings.Name = "btnSettings";
            this.btnSettings.Size = new System.Drawing.Size(28, topH);
            this.btnSettings.TabIndex = 3;
            this.btnSettings.TipsText = "Settings";
            this.btnSettings.Click += new System.EventHandler(this.btnSettings_Click);
            this.Controls.Add(this.btnSettings);
            // flowButtons
            this.flowButtons.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.flowButtons.Location = new System.Drawing.Point(12, 50);
            this.flowButtons.Name = "flowButtons";
            this.flowButtons.Size = new System.Drawing.Size(366, 32);
            this.flowButtons.Padding = new System.Windows.Forms.Padding(0);
            this.flowButtons.Margin = new System.Windows.Forms.Padding(0);
            this.flowButtons.BackColor = bg;
            this.flowButtons.WrapContents = false; // keep in one line
            this.flowButtons.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.flowButtons.AutoSize = false;
            // style helper for UIButton
            void StyleBtn(Sunny.UI.UIButton b, string textValue)
            {
                b.Text = textValue;
                b.MinimumSize = new System.Drawing.Size(64, 28);
                b.Size = new System.Drawing.Size(72, 28); // reduced width to fit one row
                b.FillColor = surface;
                b.RectColor = border;
                b.ForeColor = text;
                b.FillHoverColor = surfaceAlt;
                b.FillPressColor = accentActive;
                b.RectHoverColor = border;
                b.RectPressColor = accentActive;
                b.Cursor = System.Windows.Forms.Cursors.Hand;
                b.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            }
            StyleBtn(this.btnAdd, "Add"); this.btnAdd.Click += new System.EventHandler(this.btnAdd_Click);
            StyleBtn(this.btnCopy, "Copy"); this.btnCopy.Click += new System.EventHandler(this.btnCopy_Click);
            StyleBtn(this.btnImport, "Import"); this.btnImport.Click += new System.EventHandler(this.btnImport_Click);
            StyleBtn(this.btnExport, "Export"); this.btnExport.Click += new System.EventHandler(this.btnExport_Click);
            this.flowButtons.Controls.Add(this.btnAdd);
            this.flowButtons.Controls.Add(this.btnCopy);
            this.flowButtons.Controls.Add(this.btnImport);
            this.flowButtons.Controls.Add(this.btnExport);
            // listEntries
            this.listEntries.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.listEntries.FillColor = surface; this.listEntries.RectColor = border; this.listEntries.ForeColor = text;
            this.listEntries.Location = new System.Drawing.Point(12, 92);
            this.listEntries.Name = "listEntries";
            this.listEntries.Size = new System.Drawing.Size(366, 520);
            this.listEntries.ItemSelectBackColor = accentActive;
            this.listEntries.TabIndex = 10;
            this.listEntries.DoubleClick += new System.EventHandler(this.listEntries_DoubleClick);
            this.listEntries.KeyDown += new System.Windows.Forms.KeyEventHandler(this.listEntries_KeyDown);
            this.listEntries.MouseMove += new System.Windows.Forms.MouseEventHandler(this.listEntries_MouseMove);
            this.listEntries.MouseDown += new System.Windows.Forms.MouseEventHandler(this.listEntries_MouseDown);
            this.listEntries.MouseLeave += new System.EventHandler(this.listEntries_MouseLeave);
            this.listEntries.ContextMenuStrip = this.cmsGrid;
            // context menu
            this.cmsGrid.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.miCopy, this.miEdit, this.miDelete });
            this.cmsGrid.Name = "cmsGrid";
            this.cmsGrid.Size = new System.Drawing.Size(118, 70);
            this.miCopy.Text = "Copy"; this.miCopy.Click += new System.EventHandler(this.miCopy_Click);
            this.miEdit.Text = "Edit"; this.miEdit.Click += new System.EventHandler(this.miEdit_Click);
            this.miDelete.Text = "Delete"; this.miDelete.Click += new System.EventHandler(this.miDelete_Click);
            // status strip
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.lblStatus });
            this.statusStrip.Location = new System.Drawing.Point(0, 645);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(390, 22);
            this.statusStrip.SizingGrip = false;
            this.statusStrip.BackColor = surface;
            this.lblStatus.Text = "Ready";
            // form
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(390, 667);
            this.Controls.Add(this.flowButtons);
            this.Controls.Add(this.cmbKindFilter);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.listEntries);
            this.Controls.Add(this.btnClearSearch);
            this.Controls.Add(this.txtSearch);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
            this.MinimumSize = new System.Drawing.Size(360, 520);
            this.Name = "CmdKitForm";
            this.Text = "CmdKit";
            this.Load += new System.EventHandler(this.CmdKitForm_Load);
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.cmsGrid.ResumeLayout(false);
            this.flowButtons.ResumeLayout(false);
            this.ResumeLayout(false);
        }
    }
}
