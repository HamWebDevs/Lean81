using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Lean81
{
    // Native, event-driven drawing only: no animation loop, web view or asset loader.
    public static class Design
    {
        public static Color Canvas { get { return SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(16, 18, 21); } }
        public static Color Surface { get { return SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(25, 28, 32); } }
        public static Color Raised { get { return SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(34, 38, 43); } }
        public static Color Border { get { return SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(52, 58, 66); } }
        public static Color Text { get { return SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(238, 241, 245); } }
        public static Color Muted { get { return SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(165, 175, 188); } }
        public static Color Accent { get { return SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(196, 245, 100); } }
        public static Color OnAccent { get { return SystemInformation.HighContrast ? SystemColors.HighlightText : Color.FromArgb(20, 29, 9); } }
        public static Color Sidebar { get { return SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(11, 13, 16); } }
        public static readonly Font Small = new Font("Segoe UI", 8.5f), Body = new Font("Segoe UI", 9.5f), Strong = new Font("Segoe UI", 9.5f, FontStyle.Bold), Title = new Font("Segoe UI", 25, FontStyle.Bold), Number = new Font("Segoe UI", 27, FontStyle.Bold);
        public static void Apply(Control root)
        {
            if (root is Form) { root.BackColor = Canvas; root.ForeColor = Text; }
            else if (root is TextBoxBase) { root.BackColor = Surface; root.ForeColor = Text; }
            else if (root is TabPage) { root.BackColor = Surface; root.ForeColor = Text; }
            else if (root is ComboBox) { root.BackColor = Raised; root.ForeColor = Text; }
            else if (root is CheckBox && !(root is FeatureToggle)) { ((CheckBox)root).FlatStyle = FlatStyle.Flat; root.ForeColor = Text; }
            else if (root.BackColor == Color.White) root.BackColor = Surface;
            var grid = root as DataGridView; if (grid != null) Grid(grid);
            foreach (Control child in root.Controls) Apply(child);
        }
        public static void Grid(DataGridView grid)
        {
            grid.BackgroundColor = Surface; grid.ForeColor = Text; grid.GridColor = Border; grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Raised; grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted; grid.ColumnHeadersDefaultCellStyle.Font = Strong;
            grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(45, 57, 35);
            grid.DefaultCellStyle.SelectionForeColor = SystemInformation.HighContrast ? SystemColors.HighlightText : Text;
            grid.AlternatingRowsDefaultCellStyle.BackColor = SystemInformation.HighContrast ? Surface : Color.FromArgb(28, 31, 36);
            grid.RowHeadersDefaultCellStyle.BackColor = Surface;
        }
        public static void Box(Graphics g, Rectangle bounds, Color fill, Color border, int radius = 4)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            using (var path = new GraphicsPath())
            {
                int d = radius * 2; bounds.Width--; bounds.Height--;
                path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure();
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);
            }
        }
        public static void Glyph(Graphics g, int kind, Rectangle r, Color color)
        {
            var save = g.Save(); g.TranslateTransform(r.X, r.Y); g.ScaleTransform(r.Width / 20f, r.Height / 20f); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (kind == 11 || kind == -1) { g.DrawLines(p, new[] { new Point(3, 2), new Point(3, 17), new Point(18, 17) }); g.DrawLines(p, new[] { new Point(7, 12), new Point(11, 8), new Point(14, 11), new Point(18, 4) }); }
                else if (kind == 7) { g.DrawPolygon(p, new[] { new Point(10, 2), new Point(17, 5), new Point(16, 13), new Point(10, 18), new Point(4, 13), new Point(3, 5) }); g.DrawLines(p, new[] { new Point(6, 10), new Point(9, 13), new Point(14, 7) }); }
                else if (kind == 6 || kind == 14) { g.DrawRectangle(p, 3, 3, 14, 14); g.DrawLines(p, new[] { new Point(5, 12), new Point(8, 12), new Point(10, 6), new Point(12, 14), new Point(15, 9) }); }
                else if (kind == 13 || kind == 5) { g.DrawRectangle(p, 2, 3, 11, 10); g.DrawRectangle(p, 7, 8, 11, 10); }
                else if (kind == 3) { g.DrawEllipse(p, 3, 3, 14, 14); g.DrawLines(p, new[] { new Point(10, 6), new Point(10, 10), new Point(14, 12) }); }
                else if (kind == 8) { g.DrawRectangle(p, 2, 3, 16, 11); g.DrawLine(p, 10, 14, 10, 18); g.DrawLine(p, 6, 18, 14, 18); }
                else if (kind == 15) { g.DrawRectangle(p, 2, 6, 16, 10); g.DrawLine(p, 2, 11, 18, 11); g.DrawEllipse(p, 14, 13, 1, 1); }
                else if (kind == 16) { g.DrawRectangle(p, 5, 4, 10, 12); for (int y = 6; y <= 14; y += 4) { g.DrawLine(p, 2, y, 5, y); g.DrawLine(p, 15, y, 18, y); } }
                else if (kind == 0) { g.DrawRectangle(p, 2, 2, 6, 6); g.DrawRectangle(p, 12, 2, 6, 6); g.DrawRectangle(p, 2, 12, 6, 6); g.DrawRectangle(p, 12, 12, 6, 6); }
                else if (kind == 2) { g.DrawArc(p, 3, 3, 14, 14, -60, 300); g.DrawLine(p, 10, 1, 10, 10); }
                else if (kind == 10) { g.DrawEllipse(p, 3, 3, 14, 14); g.DrawLine(p, 10, 9, 10, 14); g.DrawLine(p, 10, 6, 10, 6.5f); }
                else { for (int y = 4; y < 18; y += 6) { g.DrawLine(p, 2, y, 18, y); int x = y == 10 ? 13 : 7; g.DrawEllipse(p, x - 2, y - 2, 4, 4); } }
            }
            g.Restore(save);
        }
    }
    public sealed class ActionControl : Button
    {
        public bool Primary; bool hover;
        public ActionControl() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Design.Canvas : Parent.BackColor);
            Color fill = Primary && Enabled ? Design.Accent : hover && Enabled ? Design.Raised : Design.Surface;
            Design.Box(e.Graphics, ClientRectangle, fill, Focused && ShowFocusCues ? Design.Accent : Primary && Enabled ? Design.Accent : Design.Border);
            TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -8, -3), !Enabled ? Design.Muted : Primary ? Design.OnAccent : Design.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, fill);
        }
    }
    public sealed class NavControl : Button
    {
        public int Page; public bool Selected; bool hover;
        public NavControl() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Selected ? Design.Raised : hover ? Design.Surface : Design.Sidebar);
            Color ink = Selected ? Design.Accent : Design.Muted;
            if (Selected) using (var b = new SolidBrush(Design.Accent)) e.Graphics.FillRectangle(b, 0, 7, 3, Height - 14);
            Design.Glyph(e.Graphics, Page, new Rectangle(13, (Height - 17) / 2, 17, 17), ink);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(40, 0, Width - 44, Height), Enabled ? ink : Design.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ink, BackColor);
        }
    }
    public sealed class BrandControl : Control
    {
        public BrandControl() { DoubleBuffered = true; Height = 112; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Design.Sidebar); Design.Glyph(e.Graphics, -1, new Rectangle(21, 24, 28, 28), Design.Accent);
            using (var font = new Font("Segoe UI", 22, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "Lean81", font, new Point(56, 17), Design.Text);
            TextRenderer.DrawText(e.Graphics, "PERFORMANCE TOOLKIT", Design.Small, new Point(23, 63), Design.Muted);
            TextRenderer.DrawText(e.Graphics, Product.Version.Replace("-rc.", " RC"), Design.Small, new Point(23, 85), Design.Accent);
        }
    }
    public sealed class SurfacePanel : Panel
    {
        public SurfacePanel() { DoubleBuffered = true; BackColor = Design.Surface; Padding = new Padding(14); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var p = new Pen(Design.Border)) e.Graphics.DrawRectangle(p, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1)); }
    }
    public sealed class FeatureToggle : CheckBox
    {
        public int FeatureIndex; public string Description; bool hover;
        public FeatureToggle() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); AutoSize = false; Height = 56; AccessibleRole = AccessibleRole.CheckButton; Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
            e.Graphics.Clear(hover ? Design.Raised : Design.Surface);
            int width = Math.Max(40, Width - 100);
            TextRenderer.DrawText(e.Graphics, Text, Design.Strong, new Rectangle(10, 7, width, 20), Design.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, Description ?? "", Design.Small, new Rectangle(10, 29, width, 20), Design.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, Checked ? "KEEP" : "UNUSED", Design.Small, new Rectangle(Width - 94, 18, 52, 22), Checked ? Design.Accent : Design.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            var track = new Rectangle(Width - 40, 20, 30, 16); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Design.Box(e.Graphics, track, Checked ? Design.Accent : Design.Raised, Checked ? Design.Accent : Design.Border, 7);
            using (var brush = new SolidBrush(Checked ? Design.OnAccent : Design.Muted)) e.Graphics.FillEllipse(brush, track.X + (Checked ? 16 : 3), track.Y + 3, 10, 10);
            using (var line = new Pen(Design.Border)) e.Graphics.DrawLine(line, 10, Height - 1, Width - 10, Height - 1);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), Design.Accent, Design.Surface);
        }
    }
    public sealed class PresetControl : Button
    {
        public string Caption; public bool Selected, Launch; bool hover;
        public PresetControl() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Design.Canvas); Design.Box(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), hover ? Design.Raised : Design.Surface, Selected || Launch || hover ? Design.Accent : Design.Border);
            TextRenderer.DrawText(e.Graphics, Text, Design.Strong, new Rectangle(13, 12, Width - 36, 24), Selected ? Design.Accent : Design.Text, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, Caption ?? "", Design.Small, new Rectangle(13, 37, Width - 23, 22), Design.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Selected) using (var b = new SolidBrush(Design.Accent)) e.Graphics.FillEllipse(b, Width - 21, 17, 6, 6);
            if (Launch) using (var p = new Pen(Design.Accent, 1.5f)) { e.Graphics.DrawLine(p, Width - 29, 22, Width - 16, 22); e.Graphics.DrawLines(p, new[] { new Point(Width - 21, 17), new Point(Width - 16, 22), new Point(Width - 21, 27) }); }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), Design.Accent, Design.Surface);
        }
    }
    public sealed class DesignComboBox : ComboBox
    {
        public DesignComboBox() { FlatStyle = FlatStyle.Flat; DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 22; BackColor = Design.Raised; ForeColor = Design.Text; }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(selected ? Design.Raised : Design.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Index < 0 ? Text : GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -4, 0), selected ? Design.Accent : Design.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }
    }
    public sealed class DesignTabs : TabControl
    {
        public DesignTabs() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Padding = new Point(14, 7); ItemSize = new Size(0, 32); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Design.Canvas);
            for (int i = 0; i < TabCount; i++)
            {
                var r = GetTabRect(i); bool selected = i == SelectedIndex;
                using (var b = new SolidBrush(selected ? Design.Surface : Design.Canvas)) e.Graphics.FillRectangle(b, r);
                TextRenderer.DrawText(e.Graphics, TabPages[i].Text, Font, r, selected ? Design.Accent : Design.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (selected) using (var p = new Pen(Design.Accent, 2)) e.Graphics.DrawLine(p, r.Left + 8, r.Bottom - 2, r.Right - 8, r.Bottom - 2);
                if (Focused && ShowFocusCues && selected) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(r, -4, -4), Design.Text, Design.Surface);
            }
        }
    }
    public sealed class StyledDialog : Form
    {
        protected override void OnLoad(EventArgs e) { Design.Apply(this); base.OnLoad(e); }
    }
    public sealed class MetricCard : Control
    {
        public string Caption, Value, Detail; public double? Ratio; public int Glyph;
        public MetricCard() { DoubleBuffered = true; BackColor = Design.Surface; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Design.Canvas); Design.Box(e.Graphics, ClientRectangle, Design.Surface, Design.Border);
            Design.Glyph(e.Graphics, Glyph, new Rectangle(Width - 35, 16, 17, 17), Design.Muted);
            TextRenderer.DrawText(e.Graphics, Caption ?? "", Design.Small, new Rectangle(16, 15, Width - 54, 21), Design.Muted, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, Value ?? "—", Design.Number, new Rectangle(12, 35, Width - 26, 51), Design.Text, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, Detail ?? "", Design.Small, new Rectangle(16, 89, Width - 32, 20), Design.Muted, TextFormatFlags.EndEllipsis);
            if (Ratio.HasValue && Product.Finite(Ratio.Value) && Ratio >= 0 && Ratio <= 1)
            {
                using (var b = new SolidBrush(Design.Raised)) e.Graphics.FillRectangle(b, 16, Height - 14, Width - 32, 3);
                using (var b = new SolidBrush(Design.Accent)) e.Graphics.FillRectangle(b, 16, Height - 14, (float)((Width - 32) * Ratio.Value), 3);
            }
        }
    }
}
