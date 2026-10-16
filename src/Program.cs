using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PSConverter
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Child codecs must return loader failures to the app instead of leaving a hidden Windows error dialog open.
            SetErrorMode(0x8003);
            if (args.Length > 0 && args[0] != "--render-preview")
            {
                AttachConsole(0xffffffff);
                try { return CommandLine.RunAsync(args).GetAwaiter().GetResult(); }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            if (args.Length > 0) Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "settings.json");
            try
            {
                var config = args.Length > 0 ? new AppConfiguration() : ConfigurationStore.Load(configPath);
                using (var form = new MainForm(config, configPath))
                {
                    if (args.Length >= 2 && args[0] == "--render-preview")
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-12000, -12000);
                        form.ShowInTaskbar = false; form.Show(); form.PerformLayout(); Application.DoEvents();
                        if (args.Length == 3) { form.AddFiles(new[] {args[2]}); form.PerformLayout(); Application.DoEvents(); }
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(PathSafety.LocalPath(args[1])); }
                    }
                    else Application.Run(form);
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (args.Length > 0) Console.Error.WriteLine(ex.ToString());
                else MessageBox.Show(ex.Message,"PS_converter",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint processId);
        [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    }
}
