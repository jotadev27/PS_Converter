using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PSConverter;

[assembly: AssemblyTitle("PS_Converter Setup")]
[assembly: AssemblyProduct("PS_Converter")]
[assembly: AssemblyCompany("jotadev27")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: ComVisible(false)]
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

internal static class SetupProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length==2 && args[0]=="--verify-package")
        {
            try {PackageInstaller.Extract(args[1],CancellationToken.None,null);return 0;}
            catch (Exception ex) {Console.Error.WriteLine(ex.Message);return 1;}
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length==2 && args[0]=="--render-pages")
        {
            string directory=PathSafety.LocalPath(args[1]); Directory.CreateDirectory(directory);
            using (var form=new SetupForm())
            {
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;form.Show();
                for (int page=0;page<5;page++)
                {
                    form.RenderPreview(page);form.PerformLayout();Application.DoEvents();
                    using (var bitmap=new Bitmap(form.Width,form.Height))
                    {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(directory,"setup-"+(page+1)+".png"));}
                }
            }
            return 0;
        }
        using (var form=new SetupForm()) Application.Run(form);
        return 0;
    }
}

[DataContract]
internal sealed class InstallationFile
{
    [DataMember] public string Name {get;set;}
    [DataMember] public long Length {get;set;}
    [DataMember] public string Sha256 {get;set;}
}

internal static class PackageInstaller
{
    internal static string ResourceText(string name)
    {
        using (Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (stream==null) throw new InvalidDataException("An installer resource is missing.");
            using (var reader=new StreamReader(stream)) return reader.ReadToEnd();
        }
    }
    internal static List<InstallationFile> Inventory()
    {
        using (Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("package.manifest"))
        {
            if (stream==null) throw new InvalidDataException("The package inventory is missing.");
            var files=(List<InstallationFile>)new DataContractJsonSerializer(typeof(List<InstallationFile>)).ReadObject(stream);
            if (files==null || files.Count==0 || files.Count>128) throw new InvalidDataException("The package inventory is invalid.");
            return files;
        }
    }
    private static string Digest(Stream stream)
    {
        using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    internal static void Extract(string destination,CancellationToken cancellation,Action<int> progress)
    {
        destination=PathSafety.LocalPath(destination);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("Choose an empty folder. Existing application files and settings are preserved.");
        using (Stream resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("portable.zip"))
        {
            if (resource==null) throw new InvalidDataException("The installation package is missing.");
            using (var archive=new ZipArchive(resource,ZipArchiveMode.Read))
            {
                List<InstallationFile> files=Inventory();
                if (archive.Entries.Count!=files.Count || files.Select(f=>f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=files.Count)
                    throw new InvalidDataException("The package inventory does not match.");
                var targets=new List<string>();
                foreach (InstallationFile file in files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string target=PathSafety.LocalPath(Path.Combine(destination,file.Name.Replace('/',Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(destination.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The package contains an unsafe path.");
                    ZipArchiveEntry entry=archive.GetEntry(file.Name);
                    if (entry==null || entry.Length!=file.Length || entry.Length>256L*1024*1024)
                        throw new InvalidDataException("The package contains an invalid file.");
                    using (Stream input=entry.Open())
                        if (!string.Equals(Digest(input),file.Sha256,StringComparison.Ordinal)) throw new InvalidDataException("A package file failed its SHA-256 check.");
                    targets.Add(target);
                }
                // Verify the entire payload before creating anything. Extraction never runs a codec or installs a system runtime.
                for (int index=0;index<files.Count;index++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string target=targets[index];Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream input=archive.GetEntry(files[index].Name).Open())
                    using (var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {
                        var buffer=new byte[65536];int count;
                        while ((count=input.Read(buffer,0,buffer.Length))!=0)
                        {cancellation.ThrowIfCancellationRequested();output.Write(buffer,0,count);}
                    }
                    if (progress!=null) progress((index+1)*100/files.Count);
                }
            }
        }
    }
}

internal sealed class SetupForm : Form
{
    private readonly Panel content=new Panel();
    private readonly Label title=Theme.Label("",22,true),description=Theme.Label("",10,false),step=Theme.Label("",9,false);
    private readonly TextBox destination=Theme.TextBox();
    private readonly TextBox terms=new TextBox();
    private readonly CheckBox accept=new CheckBox(),launch=new CheckBox();
    private readonly Button next=Theme.Button("Next",true),back=Theme.Button("Back",false),cancel=Theme.Button("Cancel",false),browse=Theme.Button("Browse…",false);
    private readonly ProgressBar progress=new ProgressBar();
    private int page;
    private bool installing;
    private CancellationTokenSource cancellation;
    private readonly InstallerArtwork artwork=new InstallerArtwork();

    internal SetupForm()
    {
        Text="PS_Converter Setup";ClientSize=new Size(840,540);FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Theme.Background;ForeColor=Theme.Text;
        Font=Theme.Font(10,FontStyle.Regular);Icon=Theme.ApplicationIcon();AutoScaleMode=AutoScaleMode.Dpi;
        artwork.Dock=DockStyle.Left;artwork.Width=236;
        var footer=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=66,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(16,12,16,12)};
        next.Width=112;back.Width=95;cancel.Width=95;
        next.Click+=async delegate {await NextAsync();};back.Click+=delegate {page--;RenderPage();};
        cancel.Click+=delegate {if (installing) cancellation.Cancel();else Close();};
        footer.Controls.Add(next);footer.Controls.Add(cancel);footer.Controls.Add(back);
        content.Dock=DockStyle.Fill;content.Padding=new Padding(28);content.BackColor=Theme.Surface;
        step.Location=new Point(30,27);step.ForeColor=Theme.Muted;
        title.Location=new Point(27,61);title.AutoSize=false;title.Size=new Size(530,80);
        description.Location=new Point(30,153);description.AutoSize=false;description.Size=new Size(520,190);description.ForeColor=Theme.Text;
        terms.Location=new Point(30,151);terms.Size=new Size(520,220);terms.Multiline=true;terms.ReadOnly=true;terms.ScrollBars=ScrollBars.Vertical;
        terms.BackColor=Theme.Surface;terms.ForeColor=Theme.Text;terms.BorderStyle=BorderStyle.FixedSingle;
        terms.Font=Theme.Font(9,FontStyle.Regular);
        terms.Text=PackageInstaller.ResourceText("license.txt").Replace("\r\n","\n").Replace("\n","\r\n");
        terms.AccessibleName="License terms and third-party notices";
        accept.Location=new Point(30,389);accept.AutoSize=true;accept.Text="I accept the license terms.";accept.AccessibleName=accept.Text;
        accept.CheckedChanged+=delegate {if (page==1) next.Enabled=accept.Checked;};
        destination.Location=new Point(30,280);destination.Size=new Size(405,30);destination.AccessibleName="Installation folder";
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PS_Converter");
        destination.Text=root;
        browse.Location=new Point(444,276);browse.Size=new Size(106,35);
        browse.Click+=delegate {using (var dialog=new FolderBrowserDialog {Description="Choose an empty application folder",SelectedPath=destination.Text})
            if (dialog.ShowDialog(this)==DialogResult.OK) destination.Text=dialog.SelectedPath;};
        progress.Location=new Point(30,295);progress.Size=new Size(520,18);progress.Style=ProgressBarStyle.Continuous;
        launch.Location=new Point(30,324);launch.AutoSize=true;launch.Text="Launch PS_Converter";launch.Checked=true;
        content.Controls.AddRange(new Control[] {step,title,description,terms,accept,destination,browse,progress,launch});
        Controls.Add(content);Controls.Add(artwork);Controls.Add(footer);AcceptButton=next;CancelButton=cancel;
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {if (installing) {e.Cancel=true;cancellation.Cancel();}};
        RenderPage();
    }
    private void RenderPage()
    {
        terms.Visible=accept.Visible=page==1;destination.Visible=browse.Visible=page==2;progress.Visible=false;launch.Visible=page==4;
        back.Visible=page>0 && page<4;next.Enabled=page!=1 || accept.Checked;cancel.Visible=page<4;
        next.Text=page==3?"Install":page==4?"Finish":"Next";step.Text=page==4?"SETUP COMPLETE":"STEP "+(page+1)+" OF 4";
        title.Text=page==0?"Welcome to\nPS_Converter":page==1?"License terms":page==2?"Choose a location":page==3?"Ready to install":"Installation complete";
        description.Visible=page!=1;
        description.Height=page==2?105:page==4?140:190;
        description.Text=page==0?"Convert audio for PSP, PS3, PS Vita and PS4.\r\n\r\nThis wizard copies the application and its included codec files to your chosen folder.\r\n\r\nNo administrator privileges are required.":
            page==2?"Choose an empty folder for the application.\r\n\r\nYour existing files and settings will not be overwritten. You can also select a removable drive.":
            page==3?"PS_Converter v1.0\r\n\r\nInstall to:\r\n"+destination.Text+"\r\n\r\nClick Install to copy the files. No system runtime installation, registry changes or startup entries are made.":
            "PS_Converter is ready in your chosen folder.\r\n\r\nOpen the application, add your audio and choose a console.\r\n\r\nKeep the codecs folder beside the application.";
    }
    private async Task NextAsync()
    {
        if (installing) return;
        if (page==4)
        {
            if (launch.Checked)
            {
                try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {FileName=Path.Combine(destination.Text,"PS_converter.exe"),WorkingDirectory=destination.Text,UseShellExecute=false});}
                catch (Exception ex) {MessageBox.Show(this,ex.Message,"Launch",MessageBoxButtons.OK,MessageBoxIcon.Error);return;}
            }
            Close();return;
        }
        if (page==1 && !accept.Checked) return;
        if (page==2)
        {
            try {string path=PathSafety.LocalPath(destination.Text);if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) throw new IOException("Choose an empty folder to preserve existing files.");destination.Text=path;}
            catch (Exception ex) {MessageBox.Show(this,ex.Message,"Installation folder",MessageBoxButtons.OK,MessageBoxIcon.Error);return;}
        }
        if (page==3)
        {
            installing=true;cancellation=new CancellationTokenSource();next.Enabled=back.Enabled=false;progress.Value=0;progress.Visible=true;
            title.Text="Installing PS_Converter";description.Text="Copying and verifying application files…";
            description.Height=100;
            var report=new Progress<int>(value=>progress.Value=value);
            try
            {
                string location=destination.Text;CancellationToken token=cancellation.Token;
                await Task.Run(()=>PackageInstaller.Extract(location,token,value=>((IProgress<int>)report).Report(value)));
                page=4;
            }
            catch (OperationCanceledException) {description.Text="Installation cancelled. Files already copied remain in the selected folder. Choose a new empty folder to retry.";return;}
            catch (Exception ex) {MessageBox.Show(this,ex.Message,"Installation",MessageBoxButtons.OK,MessageBoxIcon.Error);return;}
            finally {installing=false;cancellation.Dispose();cancellation=null;next.Enabled=back.Enabled=true;}
            RenderPage();return;
        }
        page++;RenderPage();
    }
    internal void RenderPreview(int selectedPage)
    {
        page=selectedPage;destination.Text=@"C:\Apps\PS_Converter";RenderPage();
        if (page==1 && (terms.Lines.Length<15 || next.Enabled)) throw new InvalidDataException("License text or acceptance gating failed.");
    }
}

internal sealed class InstallerArtwork : Panel
{
    private readonly List<Image> images=new List<Image>();
    internal InstallerArtwork()
    {
        DoubleBuffered=true;BackColor=Color.FromArgb(234,238,242);
        foreach (string name in new[] {"psp","ps3","vita","ps4"})
            using (Stream stream=typeof(InstallerArtwork).Assembly.GetManifestResourceStream("console."+name))
                if (stream!=null) using (Image source=Image.FromStream(stream)) images.Add(new Bitmap(source));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);Theme.DrawMark(e.Graphics,new RectangleF(76,36,84,84));
        using (var font=Theme.Font(17,FontStyle.Bold)) TextRenderer.DrawText(e.Graphics,"PS_Converter",font,new Rectangle(12,139,Width-24,36),Theme.Text,TextFormatFlags.HorizontalCenter);
        using (var font=Theme.Font(10,FontStyle.Regular)) TextRenderer.DrawText(e.Graphics,"Audio for your console",font,new Rectangle(12,183,Width-24,32),Theme.Muted,TextFormatFlags.HorizontalCenter);
        for (int i=0;i<images.Count;i++)
        {
            var area=new Rectangle(17+(i%2)*106,250+(i/2)*100,96,80);
            e.Graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(images[i],Theme.FitImage(images[i],area));
        }
    }
    protected override void Dispose(bool disposing) {if (disposing) foreach (Image image in images) image.Dispose();base.Dispose(disposing);}
}
