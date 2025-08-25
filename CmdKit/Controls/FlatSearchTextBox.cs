using System;
using System.Drawing;
using System.Windows.Forms;
using Sunny.UI;

namespace CmdKit.Controls
{
    // Flat dark-friendly search textbox; suppresses Sunny.UI focus / hover white bar.
    public class FlatSearchTextBox : UITextBox
    {
        private readonly System.Windows.Forms.Timer _enforceTimer = new() { Interval = 80 }; // refresh while focused / hovered
        private bool _initialized;

        public FlatSearchTextBox()
        {
            StyleCustomMode = true;
            Cursor = Cursors.IBeam;
            SetFlatPalette();
            _enforceTimer.Tick += (_, _) => EnforceNow();
        }

        private void EnforceNow()
        {
            if (IsDisposed || !Visible) { _enforceTimer.Stop(); return; }
            if (Focused || ClientRectangle.Contains(PointToClient(Cursor.Position)))
            {
                SetFlatPalette();
                ApplyInnerColors();
            }
            else _enforceTimer.Stop();
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            SetFlatPalette();
            ApplyInnerColors();
            _initialized = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _enforceTimer.Stop();
                _enforceTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void SetIf(string name, object value)
        {
            try { var p = GetType().GetProperty(name); p?.SetValue(this, value); } catch { }
        }

        private void SetFlatPalette()
        {
            var fill = FillColor.IsEmpty ? Color.FromArgb(40, 40, 40) : FillColor;
            FillColor = fill;
            ForeColor = ForeColor.IsEmpty ? Color.White : ForeColor;
            var rectBase = RectColor.IsEmpty ? Color.FromArgb(70, 70, 70) : RectColor;
            RectColor = rectBase;
            // unify state colors (ignore if property absent in version)
            SetIf("FillColor2", fill);
            SetIf("FillDisableColor", fill);
            SetIf("FillReadOnlyColor", fill);
            SetIf("FillHoverColor", fill);
            SetIf("FillPressColor", fill);
            SetIf("FillFocusColor", fill);
            SetIf("FillSelectedColor", fill);
            SetIf("RectDisableColor", rectBase);
            SetIf("RectReadOnlyColor", rectBase);
            SetIf("RectHoverColor", rectBase);
            SetIf("RectFocusColor", rectBase);
            SetIf("RectPressColor", rectBase);
            SetIf("ForeDisableColor", ForeColor);
            SetIf("ForeReadOnlyColor", ForeColor);
            SetIf("WatermarkColor", Color.FromArgb(140, ForeColor));
        }

        private void ApplyInnerColors()
        {
            try
            {
                var fill = FillColor.IsEmpty ? Color.FromArgb(40, 40, 40) : FillColor;
                var fore = ForeColor.IsEmpty ? Color.White : ForeColor;
                foreach (Control c in Controls)
                {
                    if (c is TextBox tb)
                    {
                        if (tb.BackColor != fill) tb.BackColor = fill;
                        if (tb.ForeColor != fore) tb.ForeColor = fore;
                        tb.BorderStyle = BorderStyle.None;
                    }
                }
            }
            catch { }
        }

        private void KickTimer() { if (!_enforceTimer.Enabled) _enforceTimer.Start(); }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); SetFlatPalette(); ApplyInnerColors(); KickTimer(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); SetFlatPalette(); ApplyInnerColors(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); SetFlatPalette(); ApplyInnerColors(); KickTimer(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); SetFlatPalette(); ApplyInnerColors(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (_initialized) ApplyInnerColors(); }
        protected override void OnEnter(EventArgs e) { base.OnEnter(e); ApplyInnerColors(); KickTimer(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); ApplyInnerColors(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); ApplyInnerColors(); }
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); ApplyInnerColors(); }
        protected override void OnLayout(LayoutEventArgs levent) { base.OnLayout(levent); if (_initialized) ApplyInnerColors(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            using var br = new SolidBrush(FillColor.IsEmpty ? Color.FromArgb(40, 40, 40) : FillColor);
            e.Graphics.FillRectangle(br, ClientRectangle);
            base.OnPaint(e);
        }
    }
}
