using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PSConverter;

internal static class BuildAssets
{
    [STAThread]
    private static void Main(string[] args)
    {
        string directory = args[0]; Directory.CreateDirectory(directory);
        string iconDirectory=Path.Combine(directory,"icons");
        foreach (string name in new[] {"upload","file-music","folder","settings-2","play","square","x","check","arrow-right"})
            SvgRasterizer.Export(Path.Combine(iconDirectory,name+".svg"),Path.Combine(iconDirectory,name+".png"),128);
        int[] sizes = {16,24,32,48,64,128,256}; var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using (var bitmap = new Bitmap(sizes[i], sizes[i], PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var stream = new MemoryStream())
            {
                graphics.Clear(Color.Transparent); Theme.DrawMark(graphics, new RectangleF(0, 0, sizes[i], sizes[i]));
                bitmap.Save(stream, ImageFormat.Png); images[i] = stream.ToArray();
                if (sizes[i] == 256) bitmap.Save(Path.Combine(directory, "app.png"), ImageFormat.Png);
            }
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(directory, "app.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }
    }
}
