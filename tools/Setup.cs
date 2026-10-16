using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using PSConverter;

// Build-time artwork only. Inno Setup owns the actual installer and its native wizard.
internal static class SetupArtwork
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected asset and output directories.");
        string assets = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        using (var bitmap = new Bitmap(492, 942, PixelFormat.Format24bppRgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(234, 238, 242));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            // Uniform scaling preserves the approved logo and console proportions.
            graphics.ScaleTransform(bitmap.Width / 236f, bitmap.Width / 236f);
            Theme.DrawMark(graphics, new RectangleF(76, 36, 84, 84));
            using (var alignment = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var title = new Font("Segoe UI", 22.67f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subtitle = new Font("Segoe UI", 13.33f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Theme.Text))
            using (var muted = new SolidBrush(Theme.Muted))
            {
                graphics.DrawString("PS_Converter", title, ink, new RectangleF(12, 139, 212, 36), alignment);
                graphics.DrawString("Audio for your console", subtitle, muted, new RectangleF(12, 183, 212, 32), alignment);
            }
            string[] consoles = { "psp", "ps3", "vita", "ps4" };
            for (int index = 0; index < consoles.Length; index++)
            {
                using (var image = Image.FromFile(Path.Combine(assets, "consoles", consoles[index] + ".png")))
                {
                    var bounds = new Rectangle(17 + (index % 2) * 106, 250 + (index / 2) * 100, 96, 80);
                    graphics.DrawImage(image, Theme.FitImage(image, bounds));
                }
            }
            bitmap.Save(Path.Combine(output, "wizard.bmp"), ImageFormat.Bmp);
        }
        using (var bitmap = new Bitmap(174, 174, PixelFormat.Format24bppRgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            Theme.DrawMark(graphics, new RectangleF(12, 12, 150, 150));
            bitmap.Save(Path.Combine(output, "wizard-small.bmp"), ImageFormat.Bmp);
        }
    }
}
