using System.Drawing;
using System.Windows.Forms;
using CmdKit.Theme;

namespace CmdKit.Controls;

public class ThemedDataGridView : DataGridView
{
    public ThemedDataGridView()
    {
        DoubleBuffered = true;
        AllowUserToResizeRows = false;
        RowHeadersVisible = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        MultiSelect = false;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        BackgroundColor = ThemeColors.Background;
        BorderStyle = BorderStyle.None;
        EnableHeadersVisualStyles = false;
        ColumnHeadersDefaultCellStyle.BackColor = ThemeColors.GridHeader;
        ColumnHeadersDefaultCellStyle.ForeColor = ThemeColors.TextPrimary;
        ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        ColumnHeadersHeight = 34;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        DefaultCellStyle.BackColor = ThemeColors.GridRow;
        DefaultCellStyle.ForeColor = ThemeColors.TextPrimary;
        DefaultCellStyle.SelectionBackColor = ThemeColors.GridSelection;
        DefaultCellStyle.SelectionForeColor = Color.Black;
        AlternatingRowsDefaultCellStyle.BackColor = ThemeColors.GridRowAlt;
        GridColor = ThemeColors.Border;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        RowTemplate.Height = 28;
    }
}
