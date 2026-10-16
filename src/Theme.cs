using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace PSConverter
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(246, 245, 242);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(220, 222, 226);
        public static readonly Color Text = Color.FromArgb(39, 43, 50);
        public static readonly Color Muted = Color.FromArgb(98, 105, 115);
        public static readonly Color Accent = Color.FromArgb(77, 98, 125);
        public static readonly Color Selected = Color.FromArgb(235, 240, 246);
        public static readonly Color Success = Color.FromArgb(57, 110, 77);
        public static Font Font(float size, FontStyle style) { return new Font("Segoe UI", size, style); }
        public static Label Label(string text, float size, bool bold)
        { return new Label { Text = text, AutoSize = true, ForeColor = Text, Font = Font(size, bold ? FontStyle.Bold : FontStyle.Regular), BackColor = Color.Transparent }; }
        public static Button Button(string text, bool primary)
        {
            var button = new ThemedButton { Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface,
                ForeColor = primary ? Color.White : Text, Font = Font(10, FontStyle.Regular), Cursor = Cursors.Hand, Height = 36,
                UseVisualStyleBackColor = false, AccessibleName = text, Primary = primary };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            return button;
        }
        public static Button IconButton(UiIcon icon, string accessibleName)
        {
            var button = (ThemedButton)Button("", false);
            button.IconKind = icon; button.AccessibleName = accessibleName;
            return button;
        }
        public static ComboBox Combo()
        {
            var combo = new BorderedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = Surface, ForeColor = Text, Font = Font(10, FontStyle.Regular), Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed };
            combo.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? Selected : Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                string value = e.Index < 0 ? combo.Text : combo.GetItemText(combo.Items[e.Index]);
                TextRenderer.DrawText(e.Graphics, value, combo.Font, e.Bounds, combo.Enabled ? Text : Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            return combo;
        }
        public static TextBox TextBox()
        { return new TextBox { BackColor = Surface, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Font = Font(10, FontStyle.Regular) }; }
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2; var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static void DrawMark(Graphics g, RectangleF bounds)
        {
            GraphicsState state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X,bounds.Y); g.ScaleTransform(bounds.Width/64f,bounds.Height/64f);
            using (var fill=new SolidBrush(Color.FromArgb(36,47,62)))
                g.FillPolygon(fill,new[] {new PointF(10,2),new PointF(54,2),new PointF(62,10),new PointF(62,54),
                    new PointF(54,62),new PointF(10,62),new PointF(2,54),new PointF(2,10)});
            using (var arrow=new SolidBrush(Color.White))
            {
                g.FillPolygon(arrow,new[] {new PointF(16,19),new PointF(42,19),new PointF(36,13),new PointF(40,9),
                    new PointF(53,22),new PointF(40,35),new PointF(36,31),new PointF(42,25),new PointF(16,25)});
                g.FillPolygon(arrow,new[] {new PointF(48,45),new PointF(22,45),new PointF(28,51),new PointF(24,55),
                    new PointF(11,42),new PointF(24,29),new PointF(28,33),new PointF(22,39),new PointF(48,39)});
            }
            g.Restore(state);
        }
        public static Icon ApplicationIcon()
        {
            using (Stream resource=typeof(Theme).Assembly.GetManifestResourceStream("app.icon"))
                if (resource!=null) using (var icon=new Icon(resource)) return (Icon)icon.Clone();
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        public static Rectangle FitImage(Image image, Rectangle bounds)
        {
            float scale = Math.Min((float)bounds.Width / image.Width, (float)bounds.Height / image.Height);
            int width = Math.Max(1, (int)(image.Width * scale)), height = Math.Max(1, (int)(image.Height * scale));
            return new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        }
    }

    internal enum UiIcon { None, Upload, Music, Folder, Settings, Play, Stop, Remove, Check, Arrow }

    internal static class UiIcons
    {
        private static readonly Dictionary<UiIcon,Image> images=new Dictionary<UiIcon,Image>();
        private static Image ImageFor(UiIcon icon)
        {
            Image result;
            if (images.TryGetValue(icon,out result)) return result;
            string name=icon==UiIcon.Upload?"upload":icon==UiIcon.Music?"file-music":icon==UiIcon.Folder?"folder":
                icon==UiIcon.Settings?"settings-2":icon==UiIcon.Play?"play":icon==UiIcon.Stop?"square":
                icon==UiIcon.Remove?"x":icon==UiIcon.Check?"check":"arrow-right";
            using (Stream stream=typeof(UiIcons).Assembly.GetManifestResourceStream("ui-icon."+name))
            {
                if (stream==null) throw new InvalidOperationException("An embedded interface icon is missing.");
                using (Image source=Image.FromStream(stream)) result=new Bitmap(source);
            }
            images.Add(icon,result); return result;
        }
        public static void Draw(Graphics graphics,UiIcon icon,RectangleF bounds,Color color)
        {
            if (icon==UiIcon.None) return;
            Image source=ImageFor(icon);
            using (var attributes=new ImageAttributes())
            {
                var tint=new ColorMatrix(); tint.Matrix00=tint.Matrix11=tint.Matrix22=0;
                tint.Matrix40=color.R/255f; tint.Matrix41=color.G/255f; tint.Matrix42=color.B/255f; tint.Matrix33=color.A/255f;
                attributes.SetColorMatrix(tint);
                graphics.DrawImage(source,Rectangle.Round(bounds),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
            }
        }
        public static Bitmap Bitmap(UiIcon icon,int size)
        {
            var image=new Bitmap(size,size);
            using (Graphics graphics=Graphics.FromImage(image)) Draw(graphics,icon,new RectangleF(3,3,size-6,size-6),Theme.Muted);
            return image;
        }
    }

    internal sealed class BorderedComboBox : ComboBox
    {
        protected override void WndProc(ref Message message)
        {
            int kind=message.Msg; base.WndProc(ref message);
            if ((kind==0x000f || kind==0x0317 || kind==0x0318) && Width>1 && Height>1)
            {
                // Native print messages use a caller-owned DC, including application-only render previews.
                using (Graphics graphics=kind==0x000f?Graphics.FromHwnd(Handle):Graphics.FromHdc(message.WParam))
                using (var pen=new Pen(Enabled?Theme.Text:Theme.Border)) graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
            }
        }
    }

    internal sealed class ThemedButton : Button
    {
        public bool Primary { get; set; }
        public UiIcon IconKind { get; set; }
        private bool hovered, pressed;
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color background = !Enabled ? Color.FromArgb(230,232,235) : pressed ? (Primary ? Color.FromArgb(65,84,108) : Color.FromArgb(221,228,237)) :
                hovered ? (Primary ? Color.FromArgb(65,84,108) : Theme.Selected) : BackColor;
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 7))
            using (var brush = new SolidBrush(background))
            using (var pen = new Pen(Primary ? background : Theme.Border))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            Color foreground = Enabled ? ForeColor : Theme.Muted;
            if (IconKind != UiIcon.None)
            {
                int left = string.IsNullOrEmpty(Text) ? (Width-20)/2 : 13;
                UiIcons.Draw(e.Graphics, IconKind, new RectangleF(left, (Height-20)/2, 20, 20), foreground);
            }
            Rectangle text = ClientRectangle;
            if (IconKind != UiIcon.None && Text.Length != 0) text = new Rectangle(37, 0, Width-45, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues && Enabled) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5,5,Width-10,Height-10));
        }
    }

    internal sealed class Card : Panel
    {
        public Card() { DoubleBuffered = true; BackColor = Theme.Surface; Padding = new Padding(16); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder(e.Graphics,ClientRectangle,Theme.Border,ButtonBorderStyle.Solid);
        }
    }

    internal sealed class BrandHeader : Panel
    {
        public BrandHeader() { DoubleBuffered = true; Height = 52; BackColor = Theme.Background; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Theme.DrawMark(e.Graphics, new RectangleF(0,3,50,50));
            using (var title = Theme.Font(19, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "PS_Converter", title, new Point(64,12), Theme.Text);
        }
    }

    internal sealed class ConsoleButton : Button
    {
        public ConsoleProfile Profile;
        public bool Selected = false;
        private Image artwork;
        public ConsoleButton(ConsoleProfile profile)
        {
            Profile = profile; DoubleBuffered = true; FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand;
            Dock = DockStyle.Fill; Margin = new Padding(5,0,5,0); AccessibleName = profile.Name;
            AccessibleDescription = "Choose " + profile.Name;
            using (Stream stream = typeof(ConsoleButton).Assembly.GetManifestResourceStream("console." + profile.Target.ToString().ToLowerInvariant()))
                if (stream != null) using (Image source = Image.FromStream(stream)) artwork = new Bitmap(source);
        }
        protected override void Dispose(bool disposing) { if (disposing && artwork != null) artwork.Dispose(); base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Background); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(1,1,Width-3,Height-3), 9))
            using (var fill = new SolidBrush(Selected ? Theme.Selected : Theme.Surface))
            using (var pen = new Pen(Selected ? Theme.Accent : Theme.Border, Selected ? 1.6f : 1))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path); }
            if (artwork != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var area = new Rectangle(8,10,Width-16,Height-46);
                e.Graphics.DrawImage(artwork, Theme.FitImage(artwork, area));
            }
            if (Selected) UiIcons.Draw(e.Graphics, UiIcon.Check, new RectangleF(Width-27,8,16,16), Theme.Accent);
            using (var font = Theme.Font(11, Selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(e.Graphics, Profile.Name, font, new Rectangle(0,Height-31,Width,25), Theme.Text, TextFormatFlags.HorizontalCenter);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5,5,Width-10,Height-10));
        }
    }

    internal sealed class DropZone : Panel
    {
        public DropZone() { DoubleBuffered = true; BackColor = Theme.Surface; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Border, 1.3f) { DashStyle = DashStyle.Dash }) e.Graphics.DrawRectangle(pen,1,1,Width-3,Height-3);
            int center = Width / 2;
            using (var brush = new SolidBrush(Theme.Selected)) e.Graphics.FillEllipse(brush, center-25,17,50,50);
            UiIcons.Draw(e.Graphics, UiIcon.Upload, new RectangleF(center-13,29,26,26), Theme.Accent);
            using (var title = Theme.Font(13, FontStyle.Bold))
            using (var small = Theme.Font(9, FontStyle.Regular))
            {
                TextRenderer.DrawText(e.Graphics, "Drop audio or click to browse", title, new Rectangle(0,76,Width,27), Theme.Text, TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(e.Graphics, "MP3  ·  WAV  ·  AT3  ·  AT9", small, new Rectangle(0,106,Width,22), Theme.Muted, TextFormatFlags.HorizontalCenter);
            }
        }
    }

    internal sealed class ProgressMeter : Control
    {
        private readonly Timer timer = new Timer { Interval = 70 };
        private int offset;
        public ProgressBarStyle Style { get; set; }
        public int Value { get; set; }
        public ProgressMeter()
        {
            DoubleBuffered = true;
            timer.Tick += delegate { offset = (offset + 8) % Math.Max(1,Width); Invalidate(); };
        }
        protected override void OnVisibleChanged(EventArgs e) { if (Visible) timer.Start(); else timer.Stop(); base.OnVisibleChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int y = Height / 2 - 2;
            using (var track = new SolidBrush(Theme.Border)) e.Graphics.FillRectangle(track,0,y,Width,4);
            using (var fill = new SolidBrush(Theme.Accent))
                if (Style == ProgressBarStyle.Marquee) e.Graphics.FillRectangle(fill,offset,y,Math.Max(10,Width/4),4);
                else e.Graphics.FillRectangle(fill,0,y,Width * Math.Min(100,Math.Max(0,Value))/100,4);
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
