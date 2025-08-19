namespace CmdKit
{
    partial class CmdKitForm
    {
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Button btnClearSearch; // clear search button
        private System.Windows.Forms.ListBox listEntries;
        private System.Windows.Forms.Button btnAdd;
        private System.Windows.Forms.Button btnCopy;
        private System.Windows.Forms.Button btnImport;
        private System.Windows.Forms.Button btnExport;
        private System.Windows.Forms.Button btnSettings;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ContextMenuStrip cmsGrid;
        private System.Windows.Forms.ToolStripMenuItem miCopy;
        private System.Windows.Forms.ToolStripMenuItem miEdit;
        private System.Windows.Forms.ToolStripMenuItem miDelete;
        private System.Windows.Forms.ComboBox cmbKindFilter;
        private System.Windows.Forms.FlowLayoutPanel flowButtons;

        private System.ComponentModel.IContainer components = null;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.btnClearSearch = new System.Windows.Forms.Button();
            this.listEntries = new System.Windows.Forms.ListBox();
            this.btnAdd = new System.Windows.Forms.Button();
            this.btnCopy = new System.Windows.Forms.Button();
            this.btnImport = new System.Windows.Forms.Button();
            this.btnExport = new System.Windows.Forms.Button();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.cmsGrid = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.miCopy = new System.Windows.Forms.ToolStripMenuItem();
            this.miEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.miDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.cmbKindFilter = new System.Windows.Forms.ComboBox();
            this.flowButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btnSettings = new System.Windows.Forms.Button();
            this.statusStrip.SuspendLayout();
            this.cmsGrid.SuspendLayout();
            this.flowButtons.SuspendLayout();
            this.SuspendLayout();
            // palette
            var bg = CmdKit.Theme.ThemeColors.Background;
            var surface = CmdKit.Theme.ThemeColors.Surface;
            var surfaceAlt = CmdKit.Theme.ThemeColors.SurfaceAlt;
            var text = CmdKit.Theme.ThemeColors.TextPrimary;
            var border = CmdKit.Theme.ThemeColors.Border;
            var accentActive = CmdKit.Theme.ThemeColors.AccentActive;
            this.BackColor = bg; this.ForeColor = text;
            int topH = 28; // unified top bar height
            // search
            this.txtSearch.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtSearch.BackColor = surface;
            this.txtSearch.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtSearch.ForeColor = text;
            this.txtSearch.Location = new System.Drawing.Point(12, 12);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.PlaceholderText = "Search...";
            this.txtSearch.Size = new System.Drawing.Size(210, topH);
            this.txtSearch.TabIndex = 0;
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            // clear search button
            this.btnClearSearch.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClearSearch.FlatAppearance.BorderSize = 0;
            this.btnClearSearch.BackColor = surface;
            this.btnClearSearch.ForeColor = text;
            this.btnClearSearch.Text = "×"; // multiplication sign
            this.btnClearSearch.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnClearSearch.Location = new System.Drawing.Point(228, 12);
            this.btnClearSearch.Name = "btnClearSearch";
            this.btnClearSearch.Size = new System.Drawing.Size(26, topH);
            this.btnClearSearch.TabIndex = 1;
            this.btnClearSearch.Visible = false;
            this.btnClearSearch.Click += (s, e) => { this.txtSearch.Text = string.Empty; this.txtSearch.Focus(); };
            // kind filter
            this.cmbKindFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbKindFilter.Items.AddRange(new object[] { "All" });
            this.cmbKindFilter.SelectedIndex = 0;
            this.cmbKindFilter.BackColor = surface; this.cmbKindFilter.ForeColor = text;
            this.cmbKindFilter.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cmbKindFilter.Location = new System.Drawing.Point(260, 12); this.cmbKindFilter.Size = new System.Drawing.Size(80, topH);
            this.cmbKindFilter.SelectedIndexChanged += new System.EventHandler(this.cmbKindFilter_SelectedIndexChanged);
            // settings button
            this.btnSettings.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSettings.FlatAppearance.BorderSize = 0;
            this.btnSettings.BackColor = surface;
            this.btnSettings.ForeColor = text;
            this.btnSettings.Text = "⚙";
            this.btnSettings.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Regular);
            this.btnSettings.Size = new System.Drawing.Size(32, topH);
            this.btnSettings.Location = new System.Drawing.Point(346, 12);
            this.btnSettings.Anchor = (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right);
            this.btnSettings.Click += new System.EventHandler(this.btnSettings_Click);
            this.Controls.Add(this.btnSettings);
            // flow buttons
            this.flowButtons.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowButtons.Location = new System.Drawing.Point(12, 48);
            this.flowButtons.Name = "flowButtons";
            this.flowButtons.Size = new System.Drawing.Size(366, 34);
            this.flowButtons.Padding = new System.Windows.Forms.Padding(0);
            this.flowButtons.Margin = new System.Windows.Forms.Padding(0);
            this.flowButtons.BackColor = bg;
            // style helper
            void StyleBtn(System.Windows.Forms.Button b)
            {
                b.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
                b.FlatAppearance.BorderColor = border;
                b.FlatAppearance.MouseOverBackColor = surfaceAlt;
                b.FlatAppearance.MouseDownBackColor = accentActive;
                b.BackColor = surface;
                b.ForeColor = text;
                b.Height = 30; b.Width = 80;
                b.Margin = new System.Windows.Forms.Padding(0,0,8,0);
            }
            // buttons (no Refresh now)
            this.btnAdd.Text = "Add"; StyleBtn(this.btnAdd); this.btnAdd.Click += new System.EventHandler(this.btnAdd_Click);
            this.btnCopy.Text = "Copy"; StyleBtn(this.btnCopy); this.btnCopy.Click += new System.EventHandler(this.btnCopy_Click);
            this.btnImport.Text = "Import"; StyleBtn(this.btnImport); this.btnImport.Click += new System.EventHandler(this.btnImport_Click);
            this.btnExport.Text = "Export"; StyleBtn(this.btnExport); this.btnExport.Click += new System.EventHandler(this.btnExport_Click);
            this.flowButtons.Controls.Add(this.btnAdd);
            this.flowButtons.Controls.Add(this.btnCopy);
            this.flowButtons.Controls.Add(this.btnImport);
            this.flowButtons.Controls.Add(this.btnExport);
            // list
            this.listEntries.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.listEntries.BackColor = surface;
            this.listEntries.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.listEntries.ForeColor = text;
            this.listEntries.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.listEntries.ItemHeight = 20;
            this.listEntries.Location = new System.Drawing.Point(12, 88);
            this.listEntries.Name = "listEntries";
            this.listEntries.Size = new System.Drawing.Size(366, 520);
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
            this.MinimumSize = new System.Drawing.Size(320, 520);
            this.Name = "CmdKitForm";
            this.Text = "CmdKit";
            this.Load += new System.EventHandler(this.CmdKitForm_Load);
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.cmsGrid.ResumeLayout(false);
            this.flowButtons.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
