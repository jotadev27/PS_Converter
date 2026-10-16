using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PSConverter
{
    internal sealed class MainForm : Form
    {
        private readonly AppConfiguration configuration;
        private readonly string configPath;
        private readonly ConversionEngine engine;
        private readonly ToolRegistry registry;
        private ConsoleProfile profile = ConsoleProfile.For(ConsoleTarget.Vita);
        private readonly List<ConsoleButton> consoles = new List<ConsoleButton>();
        private readonly ComboBox output = Theme.Combo();
        private int bitrateKbps;
        private readonly TextBox outputDirectory = Theme.TextBox();
        private readonly ListView queue = new ListView();
        private readonly Label status = Theme.Label("Add audio to start.", 9, false);
        private readonly Label queueCount = Theme.Label("", 10, true);
        private readonly ProgressMeter progress = new ProgressMeter();
        private readonly Button convert = Theme.Button("Convert to AT9", true);
        private readonly Button preview = Theme.IconButton(UiIcon.Play, "Play selected audio");
        private readonly Button stop = Theme.IconButton(UiIcon.Stop, "Stop playback");
        private readonly Button remove = Theme.IconButton(UiIcon.Remove, "Remove selected files from the list");
        private readonly Button folder = Theme.Button("Audio folder", false);
        private readonly Button setup = Theme.Button("Details", false);
        private readonly List<Control> editable = new List<Control>();
        private readonly ToolTip tips = new ToolTip();
        private readonly DropZone drop = new DropZone();
        private readonly Card filesCard = new Card();
        private ImageList fileIcons;
        private Bitmap noteIcon;
        private CancellationTokenSource cancellation;
        private bool busy, closing;
        private WavePlayback player;

        public MainForm(AppConfiguration configuration, string configPath, ToolRegistry registry = null)
        {
            this.configuration = configuration; this.configPath = configPath;
            this.registry = registry ?? new ToolRegistry(configuration);
            engine = new ConversionEngine(this.registry);
            Text = "PS_Converter"; ClientSize = new Size(760, 580); MinimumSize = new Size(730, 617);
            AutoScaleMode = AutoScaleMode.Dpi; BackColor = Theme.Background; ForeColor = Theme.Text;
            Font = Theme.Font(10, FontStyle.Regular); StartPosition = FormStartPosition.CenterScreen;
            Icon = Theme.ApplicationIcon(); AllowDrop = true;
            DragEnter += OnDragEnter; DragDrop += OnDragDrop; FormClosing += OnClosing;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(23,10,23,18), ColumnCount = 1, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach (int height in new[] {64,26,144,36,120,68,64,28})
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.RowCount = 9;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.Controls.Add(BuildHeader(),0,0);
            layout.Controls.Add(Section("1  Audio"),0,1);
            layout.Controls.Add(BuildAudioArea(),0,2);
            layout.Controls.Add(Section("2  Console"),0,3);
            var devices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0) };
            for (int i = 0; i < 4; i++)
            {
                devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
                var device = new ConsoleButton(ConsoleProfile.All[i]);
                device.Click += delegate(object sender, EventArgs args) { SelectConsole(((ConsoleButton)sender).Profile); };
                devices.Controls.Add(device,i,0); consoles.Add(device); editable.Add(device);
            }
            layout.Controls.Add(devices,0,4); layout.Controls.Add(BuildOutputRow(),0,5);
            layout.Controls.Add(BuildActions(),0,6); layout.Controls.Add(BuildStatus(),0,7);
            Controls.Add(layout);
            output.Name = "OutputFormat";
            output.AccessibleName="Output format";
            output.Items.AddRange(new object[] {"AT9","WAV","MP3"}); output.SelectedIndex = 0;
            output.SelectedIndexChanged += delegate { RefreshProfile(); };
            SelectConsole(profile); stop.Enabled = false; RefreshReadiness();
            AcceptButton = convert;
        }

        private static Label Section(string title)
        {
            var label = Theme.Label(title,11,true); label.Dock = DockStyle.Fill;
            label.AutoSize = false; label.TextAlign = ContentAlignment.BottomLeft; label.Padding = new Padding(0,0,0,7);
            return label;
        }

        private Control BuildHeader()
        {
            var header = new BrandHeader { Dock = DockStyle.Fill };
            var options = Theme.Button("Options",false); ((ThemedButton)options).IconKind = UiIcon.Settings;
            options.Size = new Size(105,35); options.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.SizeChanged += delegate { options.Location = new Point(header.Width-options.Width,7); };
            options.Click += delegate { ShowOptions(false); };
            header.Controls.Add(options); editable.Add(options);
            return header;
        }

        private Control BuildAudioArea()
        {
            var host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Background };
            drop.Dock = DockStyle.Fill; drop.AllowDrop = true; drop.AccessibleName = "Add audio";
            drop.TabStop = true;
            drop.KeyDown += delegate(object sender,KeyEventArgs e) {
                if (!busy && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)) { AddFilesDialog(); e.Handled = true; }
            };
            drop.DragEnter += OnDragEnter; drop.DragDrop += OnDragDrop; drop.Click += delegate { if (!busy) AddFilesDialog(); };
            filesCard.Dock = DockStyle.Fill; filesCard.Padding = new Padding(10,6,10,6); filesCard.Visible = false;
            var listLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0)};
            listLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,42)); listLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount=1, Margin=new Padding(0) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,108));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,38));
            queueCount.AutoEllipsis=true;
            queueCount.Dock = DockStyle.Fill; queueCount.AutoSize = false; queueCount.TextAlign = ContentAlignment.MiddleLeft;
            var add = Theme.Button("Add files",false); add.Name="AddAudio"; add.Dock = DockStyle.Fill; add.Margin = new Padding(3,0,7,8);
            add.Click += delegate { AddFilesDialog(); };
            remove.Dock = DockStyle.Fill; remove.Margin = new Padding(0,0,0,8);
            remove.Click += delegate { RemoveSelected(); };
            tips.SetToolTip(remove,"Remove selected from the list");
            toolbar.Controls.Add(queueCount,0,0); toolbar.Controls.Add(add,1,0); toolbar.Controls.Add(remove,2,0);
            queue.Name = "AudioQueue"; queue.Dock = DockStyle.Fill; queue.BackColor = Theme.Surface; queue.ForeColor = Theme.Text;
            queue.Margin=new Padding(0,4,0,0); queue.AccessibleName="Audio files and conversion status";
            queue.View = View.Details; queue.HeaderStyle = ColumnHeaderStyle.None;
            queue.FullRowSelect = true; queue.MultiSelect = true; queue.HideSelection = false; queue.ShowItemToolTips = true;
            queue.BorderStyle = BorderStyle.None; queue.Columns.Add("File",395); queue.Columns.Add("Input",0); queue.Columns.Add("Status",250);
            fileIcons = new ImageList { ImageSize = new Size(28,30), ColorDepth = ColorDepth.Depth32Bit };
            // ImageList defers creating its native handle. Keep the source bitmap alive until the list is disposed.
            noteIcon = UiIcons.Bitmap(UiIcon.Music,28); fileIcons.Images.Add(noteIcon);
            queue.SmallImageList = fileIcons; queue.AllowDrop = true; queue.DragEnter += OnDragEnter; queue.DragDrop += OnDragDrop;
            queue.SelectedIndexChanged += delegate { preview.Enabled = !busy && queue.SelectedItems.Count > 0; remove.Enabled = !busy && queue.SelectedItems.Count > 0; };
            queue.KeyDown += delegate(object sender,KeyEventArgs e) { if (e.KeyCode == Keys.Delete && !busy) RemoveSelected(); };
            queue.SizeChanged += delegate { queue.Columns[2].Width=190; queue.Columns[0].Width = Math.Max(160,queue.ClientSize.Width-195); };
            listLayout.Controls.Add(toolbar,0,0); listLayout.Controls.Add(queue,0,1); filesCard.Controls.Add(listLayout);
            host.Controls.Add(filesCard); host.Controls.Add(drop); editable.Add(add); editable.Add(remove); editable.Add(drop);
            return host;
        }

        private Control BuildOutputRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(0,21,0,8), Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,65)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,67)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,105));
            var to = Theme.Label("Save to",10,false); to.Dock = DockStyle.Fill; to.AutoSize = false; to.TextAlign = ContentAlignment.MiddleLeft;
            folder.Dock = DockStyle.Fill; folder.Margin = new Padding(0,0,16,0); ((ThemedButton)folder).IconKind = UiIcon.Folder;
            folder.Click += delegate {
                using (var dialog = new FolderBrowserDialog { Description = "Choose the output folder", SelectedPath = outputDirectory.Text })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        outputDirectory.Text = dialog.SelectedPath; folder.Text = Path.GetFileName(dialog.SelectedPath.TrimEnd('\\'));
                        if (folder.Text.Length == 0) folder.Text = dialog.SelectedPath;
                        tips.SetToolTip(folder,dialog.SelectedPath);
                    }
            };
            var format = Theme.Label("Format",10,false); format.Dock = DockStyle.Fill; format.AutoSize = false; format.TextAlign = ContentAlignment.MiddleLeft;
            output.Margin = new Padding(0,6,0,0);
            row.Controls.Add(to,0,0); row.Controls.Add(folder,1,0); row.Controls.Add(format,2,0); row.Controls.Add(output,3,0);
            editable.Add(folder); editable.Add(output);
            return row;
        }

        private Control BuildActions()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount=2, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,300));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            convert.Name = "ConvertAudio"; convert.Dock = DockStyle.Fill; convert.Margin = new Padding(0,6,0,6);
            convert.Font = Theme.Font(12,FontStyle.Bold);
            convert.Click += async delegate { if (busy) { if (cancellation != null) cancellation.Cancel(); } else await ConvertQueueAsync(); };
            progress.Dock = DockStyle.Fill; progress.Visible = false; progress.Margin = new Padding(0,3,0,3);
            row.Controls.Add(convert,1,0); row.Controls.Add(progress,0,1); row.SetColumnSpan(progress,3);
            return row;
        }

        private Control BuildStatus()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,85));
            status.Dock = DockStyle.Fill; status.AutoSize = false; status.ForeColor = Theme.Muted; status.TextAlign = ContentAlignment.MiddleLeft;
            status.Name = "ConversionStatus";
            setup.Anchor = AnchorStyles.Top | AnchorStyles.Right; setup.Size = new Size(80,26); setup.Margin = new Padding(0); setup.Visible = false;
            setup.Click += delegate { ShowOptions(true); };
            row.Controls.Add(status,0,0); row.Controls.Add(setup,1,0); editable.Add(setup);
            return row;
        }

        private void SelectConsole(ConsoleProfile selected)
        {
            if (busy) return;
            if (profile.Target != selected.Target) configuration.Options = new CodecOptions();
            profile = selected;
            foreach (ConsoleButton button in consoles) { button.Selected = button.Profile.Target == selected.Target; button.Invalidate(); }
            bitrateKbps = profile.DefaultBitrate;
            if (output.Items.Count != 0) output.Items[0] = profile.IsAt9 ? "AT9" : "AT3";
            RefreshProfile();
        }
        private void RefreshProfile()
        {
            convert.Text = busy ? "Cancel" : "Convert to " + (output.SelectedIndex == 0 ? profile.Extension.Substring(1).ToUpperInvariant() : output.SelectedIndex == 1 ? "WAV" : "MP3");
            RefreshReadiness();
        }
        private void RefreshReadiness()
        {
            if (busy) { convert.Enabled = true; return; }
            bool hasFiles = queue.Items.Count > 0;
            string missing = MissingEncoders().FirstOrDefault();
            bool ready = missing == null;
            setup.Visible = hasFiles && !ready;
            convert.Enabled = hasFiles;
            output.Enabled = hasFiles && queue.Items.Cast<ListViewItem>().Any(i => IsEncoded((string)i.Tag));
            preview.Enabled = queue.SelectedItems.Count > 0;
            remove.Enabled = queue.SelectedItems.Count > 0;
            foreach (ListViewItem item in queue.Items)
                if (item.SubItems[2].Text == "Ready" || item.SubItems[2].Text == "Missing files")
                    item.SubItems[2].Text = ready ? "Ready" : "Missing files";
            status.Text = !hasFiles ? "Add audio to start." : !ready ? "Missing encoder files. Extract the complete portable ZIP." : queue.Items.Count + " file(s) ready.";
            tips.SetToolTip(convert,"Convert the audio files");
        }
        private IEnumerable<string> MissingEncoders()
        {
            if (!registry.Has(profile.Executable)) yield return profile.Executable;
            if (output.SelectedIndex == 2 && !registry.Has("lame.exe")) yield return "lame.exe";
        }
        private bool EnsureEncoders()
        {
            // Convert must never ask the user to find a program. Custom tool selection is confined to Options.
            if (!MissingEncoders().Any()) return true;
            RefreshReadiness();
            return false;
        }
        private static bool IsEncoded(string path)
        { string extension = Path.GetExtension(path).ToLowerInvariant(); return extension == ".at3" || extension == ".at9"; }

        private void ShowOptions(bool toolTab)
        {
            try
            {
                var draft = new AppConfiguration { Options = configuration.Options.Copy(), Tools = new List<ApprovedTool>(configuration.Tools) };
                using (var dialog = new OptionsForm(draft,toolTab,profile,bitrateKbps,output.SelectedIndex != 0))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    draft.Options = dialog.Options; ConfigurationStore.Save(configPath,draft);
                    configuration.Tools = draft.Tools; configuration.Options = draft.Options; bitrateKbps = dialog.Bitrate;
                    RefreshReadiness();
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }
        private void OnDragEnter(object sender,DragEventArgs e) { e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; }
        private void OnDragDrop(object sender,DragEventArgs e) { if (!busy) AddFiles((string[])e.Data.GetData(DataFormats.FileDrop)); }
        private void AddFilesDialog()
        {
            using (var dialog = new OpenFileDialog { Multiselect = true, Filter = "Audio|*.mp3;*.wav;*.at3;*.at9", Title = "Add audio" })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames);
        }
        internal void AddFiles(IEnumerable<string> paths)
        {
            bool wasEmpty = queue.Items.Count == 0;
            foreach (string path in paths)
            {
                try
                {
                    string local = PathSafety.LocalPath(path), extension = Path.GetExtension(local).ToLowerInvariant();
                    if (!File.Exists(local) || Array.IndexOf(new[] {".wav",".mp3",".at3",".at9"},extension) < 0) continue;
                    if (queue.Items.Cast<ListViewItem>().Any(i => string.Equals((string)i.Tag,local,StringComparison.OrdinalIgnoreCase))) continue;
                    var item = new ListViewItem(new[] {Path.GetFileName(local),extension.Substring(1).ToUpperInvariant(),"Ready"}) { Tag = local, ImageIndex = 0 };
                    queue.Items.Add(item);
                    if (outputDirectory.Text.Length == 0) outputDirectory.Text = Path.GetDirectoryName(local);
                }
                catch (Exception ex) { status.Text = ex.Message; }
            }
            if (wasEmpty && queue.Items.Count > 0)
            {
                string first = (string)queue.Items[0].Tag;
                if (IsEncoded(first))
                {
                    string extension = Path.GetExtension(first).ToLowerInvariant();
                    if (profile.Extension != extension) SelectConsole(ConsoleProfile.For(extension == ".at3" ? ConsoleTarget.PSP : ConsoleTarget.Vita));
                    output.SelectedIndex = 1;
                }
                else output.SelectedIndex = 0;
            }
            RefreshAudioArea(); RefreshProfile();
        }
        private void RefreshAudioArea()
        {
            bool hasFiles = queue.Items.Count > 0;
            filesCard.Visible = hasFiles; drop.Visible = !hasFiles;
            if (hasFiles) filesCard.BringToFront(); else drop.BringToFront();
            queueCount.Text = queue.Items.Count + (queue.Items.Count == 1 ? " audio file" : " audio files");
        }
        private void RemoveSelected()
        {
            foreach (ListViewItem item in queue.SelectedItems.Cast<ListViewItem>().ToArray()) queue.Items.Remove(item);
            RefreshAudioArea(); RefreshReadiness();
        }
        private void SetBusy(bool value)
        {
            busy = value; foreach (Control control in editable) control.Enabled = !value;
            progress.Visible = value;
            RefreshProfile();
            if (!value) { progress.Style = ProgressBarStyle.Continuous; progress.Value = 0; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { tips.Dispose(); outputDirectory.Dispose(); if (fileIcons != null) fileIcons.Dispose(); if (noteIcon != null) noteIcon.Dispose(); }
            base.Dispose(disposing);
        }

        private async Task ConvertQueueAsync()
        {
            if (busy) return;
            if (queue.Items.Count == 0) { status.Text = "Add at least one audio file."; return; }
            int kbps = bitrateKbps;
            if (output.SelectedIndex == 0 && kbps <= 0) { status.Text = "Choose a positive bitrate in Options."; return; }
            string destination;
            try { destination = PathSafety.LocalPath(outputDirectory.Text); if (!Directory.Exists(destination)) throw new IOException("Choose an existing output folder."); }
            catch (Exception ex) { ShowError(ex); return; }
            if (!EnsureEncoders()) return;
            StopPlayer(); SetBusy(true); cancellation = new CancellationTokenSource();
            var snapshot = queue.Items.Cast<ListViewItem>().ToArray();
            int succeeded = 0, failed = 0, completed = 0;
            OutputKind kind = (OutputKind)output.SelectedIndex; CodecOptions options = configuration.Options.Copy();
            var report = new Progress<string>(text => { if (!IsDisposed) status.Text = text + "  /  " + completed + " of " + snapshot.Length + " finished"; });
            try
            {
                foreach (ListViewItem item in snapshot)
                {
                    cancellation.Token.ThrowIfCancellationRequested(); item.SubItems[2].Text = "Converting...";
                    progress.Style = ProgressBarStyle.Marquee;
                    try
                    {
                        var result = await engine.ConvertAsync(new ConversionRequest { Input = (string)item.Tag, OutputDirectory = destination,
                            Target = profile.Target, Output = kind, Bitrate = kbps, Options = options },
                            text => ((IProgress<string>)report).Report(text), cancellation.Token);
                        item.SubItems[2].Text = "Saved: " + Path.GetFileName(result.OutputPath); item.ForeColor = Theme.Success;
                        item.ToolTipText = result.OutputPath; item.Name = result.OutputPath; succeeded++;
                    }
                    catch (OperationCanceledException) { item.SubItems[2].Text = "Cancelled"; throw; }
                    catch (Exception ex) { item.SubItems[2].Text = "Failed: " + ex.Message; item.ForeColor = Color.FromArgb(150, 65, 61); failed++; }
                    completed++;
                }
                status.Text = succeeded + " converted" + (failed == 0 ? "." : ", " + failed + " failed.");
            }
            catch (OperationCanceledException) { status.Text = "Cancelled."; }
            finally
            {
                string completion = status.Text;
                cancellation.Dispose(); cancellation = null; SetBusy(false); status.Text = completion;
                if (closing) Close();
            }
        }

        private async Task PlaySelectedAsync()
        {
            if (busy || queue.SelectedItems.Count == 0) { status.Text = "Select an audio file to preview."; return; }
            string path = queue.SelectedItems[0].Name;
            if (string.IsNullOrEmpty(path)) path = (string)queue.SelectedItems[0].Tag;
            StopPlayer(); SetBusy(true); cancellation = new CancellationTokenSource();
            try
            {
                string workspaceRoot = string.IsNullOrEmpty(outputDirectory.Text) ? Path.GetDirectoryName(path) : outputDirectory.Text;
                string workspace = PathSafety.NewWorkspace(PathSafety.LocalPath(workspaceRoot));
                string wave;
                if (Path.GetExtension(path).Equals(".at3", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".at9", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await engine.ConvertAsync(new ConversionRequest { Input = path, OutputDirectory = workspace, Target = profile.Target,
                        Output = OutputKind.WAV, Options = configuration.Options.Copy() }, text => { }, cancellation.Token);
                    wave = result.OutputPath;
                }
                else
                {
                    wave = Path.Combine(workspace, "preview.wav");
                    await Task.Run(() => WindowsAudio.Normalize(path, wave, 48000, 2, cancellation.Token));
                }
                cancellation.Token.ThrowIfCancellationRequested();
                player = new WavePlayback(); player.Play(wave); stop.Enabled = true;
                status.Text = "Playing audio.";
            }
            catch (OperationCanceledException) { status.Text = "Preview cancelled."; }
            catch (Exception ex) { ShowError(ex); }
            finally { string completion = status.Text; cancellation.Dispose(); cancellation = null; SetBusy(false); status.Text = completion; if (closing) Close(); }
        }
        private void StopPlayer() { if (player != null) { player.Stop(); player.Dispose(); player = null; } stop.Enabled = false; }
        private void ShowError(Exception ex) { status.Text = ex.Message; MessageBox.Show(this, ex.Message, "PS_converter", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (busy) { closing = true; e.Cancel = true; if (cancellation != null) cancellation.Cancel(); }
            else StopPlayer();
        }
    }
}
