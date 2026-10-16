using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PSConverter;

internal static class RenderPresentation
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string output=PathSafety.LocalPath(args[0]); Directory.CreateDirectory(output);
        string package=PathSafety.LocalPath(args[1]);
        var config=new AppConfiguration();
        using (var form=new MainForm(config,Path.Combine(output,"unused-settings.json"),new ToolRegistry(config,package)))
        {
            Prepare(form); Save(form,Path.Combine(output,"main.png"));
            if (args.Length>2)
            {
                form.AddFiles(new[] {PathSafety.LocalPath(args[2])});
                Save(form,Path.Combine(output,"audio-ready.png"));
                form.Width=1024; Save(form,Path.Combine(output,"wide.png"));
            }
        }
        using (var form=new OptionsForm(new AppConfiguration(),false,ConsoleProfile.For(ConsoleTarget.PS3),128,false))
        { Prepare(form); Save(form,Path.Combine(output,"options.png")); }
        return 0;
    }
    private static void Prepare(Form form)
    { form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-12000,-12000); form.ShowInTaskbar=false; form.Show(); }
    private static void Save(Form form,string path)
    {
        form.PerformLayout(); Application.DoEvents();
        using (var bitmap=new Bitmap(form.Width,form.Height))
        {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(path);}
    }
}
