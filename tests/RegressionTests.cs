using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PSConverter
{
    internal static class Tests
    {
        private static int passed, failed;
        private static string root, fixtures;
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("-", StringComparison.Ordinal)) return Fixture(args);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Control.CheckForIllegalCrossThreadCalls = true;
            // Direct PerformClick tests need the same UI dispatch context that Application.Run installs in production.
            WindowsFormsSynchronizationContext.AutoInstall = false;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            root = Path.Combine(args[0], Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            fixtures = Path.Combine(root, "fixture-codecs"); Directory.CreateDirectory(fixtures);
            foreach (string name in ConsoleProfile.All.Select(p => p.Executable).Concat(new[] { "lame.exe" }))
                File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, Path.Combine(fixtures, name));
            Run("Platform profiles and defaults", delegate {
                Assert(ConsoleProfile.For(ConsoleTarget.PSP).SampleRate == 44100, "PSP rate");
                Assert(ConsoleProfile.For(ConsoleTarget.Vita).DefaultBitrate == 144, "Vita preset");
                foreach (var p in ConsoleProfile.All) Assert(p.Bitrates.Distinct().Count() == p.Bitrates.Length, "Duplicate bitrate");
            });
            Run("Codec-specific options and large repeat count", delegate {
                var o = new CodecOptions { Repeats = 1000, Loop = LoopKind.Custom, LoopStart = 0, LoopEnd = 7000 };
                var p = ConsoleProfile.For(ConsoleTarget.PSP);
                Assert(CodecArguments.Encode(p, 132, o, "a.wav", "b.at3").Contains("7000"), "Custom loop");
                Assert(CodecArguments.Decode(p, o, "a.at3", "b.wav").Contains("1000"), "Repeat cap");
                o.LoopEnd = 5; Throws<ArgumentException>(() => CodecArguments.Validate(p, o, true));
                o = new CodecOptions { BandExtension = true, WideBand = true };
                Throws<ArgumentException>(() => CodecArguments.Validate(ConsoleProfile.For(ConsoleTarget.PS4), o, true));
                o = new CodecOptions { SampleRate = 44100 };
                CodecArguments.Validate(ConsoleProfile.For(ConsoleTarget.Vita), o, true);
                Throws<ArgumentException>(() => CodecArguments.Validate(p, o, true));
            });
            Run("Unsafe path forms rejected", delegate {
                Throws<ArgumentException>(() => PathSafety.LocalPath(@"\\server\share\audio.wav"));
                Throws<ArgumentException>(() => PathSafety.LocalPath(Path.Combine(root, "audio.wav") + ":payload"));
                Throws<ArgumentException>(() => PathSafety.LocalPath("bad\npath"));
            });
            Run("Atomic publication preserves collisions", delegate {
                string output = Path.Combine(root, "publication"); Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "track.wav"), "existing");
                string staged = Path.Combine(root, "staged.wav"); File.WriteAllText(staged, "new");
                string result = PathSafety.Publish(staged, output, "track", ".wav");
                Assert(Path.GetFileName(result) == "track (1).wav", "Suffix");
                Assert(File.ReadAllText(Path.Combine(output, "track.wav")) == "existing", "Overwrite");
            });
            Run("Streaming PCM WAV header and malformed output", delegate {
                string wave = Path.Combine(root, "tone.wav"); WriteTone(wave, 22050, 1);
                WaveInfo info = WaveFile.Inspect(wave, false, ".wav");
                Assert(info.SampleRate == 22050 && info.Channels == 1 && info.Frames == 5512, "PCM header");
                string invalid = Path.Combine(root, "invalid.at3"); File.WriteAllText(invalid, "not audio");
                Throws<InvalidDataException>(() => WaveFile.Inspect(invalid, true, ".at3"));
                Throws<InvalidDataException>(() => WaveFile.Inspect(wave, true, ".at9"));
            });
            Run("Real Windows PCM resampling and stereo normalization", delegate {
                string output = Path.Combine(root, "normalized.wav");
                WindowsAudio.Normalize(Path.Combine(root, "tone.wav"), output, 44100, 2, CancellationToken.None);
                WaveInfo info = WaveFile.Inspect(output, false, ".wav");
                Assert(info.SampleRate == 44100 && info.Channels == 2 && info.BitsPerSample == 16, "Negotiated PCM");
                Assert(info.Frames > 10000 && info.Frames < 12000, "Duration changed");
            });
            Run("Real Windows float WAV to PCM16 normalization", delegate {
                string input = Path.Combine(root, "float.wav"), output = Path.Combine(root, "float-pcm.wav");
                WriteFloat(input); WindowsAudio.Normalize(input, output, 48000, 0, CancellationToken.None);
                var info = WaveFile.Inspect(output, false, ".wav"); Assert(info.BitsPerSample == 16 && info.Channels == 1, "Float normalization");
            });
            Run("Real Windows MP3 decoding with generated silent frames", delegate {
                string input = Path.Combine(root, "silent.mp3"), output = Path.Combine(root, "silent-pcm.wav");
                WriteSilentMp3(input); WaveFile.ValidateMp3(input);
                WindowsAudio.Normalize(input, output, 48000, 2, CancellationToken.None);
                var info = WaveFile.Inspect(output, false, ".wav"); Assert(info.Frames > 10000 && info.Channels == 2, "MP3 decoding");
            });
            Run("Process argument quoting round trips without a shell", delegate {
                var config = FixtureConfiguration(); var registry = new ToolRegistry(config);
                string output = Path.Combine(root, "arguments.txt");
                string[] values = { "space value", @"trailing\", "embedded\"quote", "& echo injected", "" };
                using (var tool = registry.Open("PSP_at3tool.exe"))
                    new ProcessRunner().RunAsync(tool, new[] { "--fixture-arguments", output }.Concat(values), root, 10, CancellationToken.None).GetAwaiter().GetResult();
                Assert(File.ReadAllLines(output).SequenceEqual(values), "Argument corruption");
            });
            Run("Large stdout and stderr are drained", delegate {
                using (var tool = new ToolRegistry(FixtureConfiguration()).Open("PSP_at3tool.exe"))
                    new ProcessRunner().RunAsync(tool, new[] { "--fixture-flood" }, root, 10, CancellationToken.None).GetAwaiter().GetResult();
            });
            Run("Process timeout terminates codec", delegate {
                using (var tool = new ToolRegistry(FixtureConfiguration()).Open("PSP_at3tool.exe"))
                    Throws<TimeoutException>(() => new ProcessRunner().RunAsync(tool, new[] { "--fixture-hang" }, root, 1, CancellationToken.None).GetAwaiter().GetResult());
            });
            Run("Cancellation terminates codec", delegate {
                using (var tool = new ToolRegistry(FixtureConfiguration()).Open("PSP_at3tool.exe"))
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(300);
                    Throws<OperationCanceledException>(() => new ProcessRunner().RunAsync(tool, new[] { "--fixture-hang" }, root, 0, cts.Token).GetAwaiter().GetResult());
                }
            });
            Run("Cancellation terminates attached child processes", delegate {
                string pidFile = Path.Combine(root, "fixture-child.pid");
                using (var tool = new ToolRegistry(FixtureConfiguration()).Open("PSP_at3tool.exe"))
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(1500);
                    Throws<OperationCanceledException>(() => new ProcessRunner().RunAsync(tool, new[] { "--fixture-parent", pidFile }, root, 0, cts.Token).GetAwaiter().GetResult());
                }
                Assert(File.Exists(pidFile), "Child was not started");
                int pid = int.Parse(File.ReadAllText(pidFile));
                try { using (var child = Process.GetProcessById(pid)) Assert(child.WaitForExit(2000), "Child survived cancellation"); }
                catch (ArgumentException) { }
            });
            Run("Nonzero codec exit is surfaced", delegate {
                using (var tool = new ToolRegistry(FixtureConfiguration()).Open("PSP_at3tool.exe"))
                    Throws<InvalidOperationException>(() => new ProcessRunner().RunAsync(tool, new[] { "--fixture-fail" }, root, 0, CancellationToken.None).GetAwaiter().GetResult());
            });
            Run("Configuration persistence round trip", delegate {
                string path = Path.Combine(root, "settings.json"); var config = FixtureConfiguration(); config.Options.Repeats = 1000;
                ConfigurationStore.Save(path, config); ConfigurationStore.Save(path, config);
                var loaded = ConfigurationStore.Load(path);
                Assert(loaded.Options.Repeats == 1000 && loaded.Tools.Count == 5, "Settings lost");
            });
            Run("Eight conversion routes using explicitly synthetic codec fixtures", delegate {
                var config = FixtureConfiguration(); var engine = new ConversionEngine(new ToolRegistry(config));
                string directory = Path.Combine(root, "routes"); Directory.CreateDirectory(directory);
                string mp3 = Path.Combine(root, "silent.mp3"), wav = Path.Combine(root, "tone.wav");
                foreach (ConsoleTarget target in new[] { ConsoleTarget.PSP, ConsoleTarget.Vita })
                {
                    var profile = ConsoleProfile.For(target);
                    string encoded = null;
                    foreach (string source in new[] { wav, mp3 })
                    {
                        var result = engine.ConvertAsync(new ConversionRequest { Input = source, OutputDirectory = directory, Target = target,
                            Output = OutputKind.ATRAC, Bitrate = profile.DefaultBitrate, Options = new CodecOptions() }, text => { }, CancellationToken.None).GetAwaiter().GetResult();
                        WaveFile.Inspect(result.OutputPath, true, profile.Extension); encoded = result.OutputPath;
                    }
                    foreach (OutputKind format in new[] { OutputKind.WAV, OutputKind.MP3 })
                    {
                        var result = engine.ConvertAsync(new ConversionRequest { Input = encoded, OutputDirectory = directory, Target = target,
                            Output = format, Options = new CodecOptions() }, text => { }, CancellationToken.None).GetAwaiter().GetResult();
                        Assert(File.Exists(result.OutputPath), "Missing output");
                    }
                }
                Assert(WaveFile.Inspect(wav, false, ".wav").SampleRate == 22050, "Source overwritten");
            });
            Run("Zero exit without output is rejected", delegate {
                var engine = new ConversionEngine(new ToolRegistry(FixtureConfiguration()));
                Throws<FileNotFoundException>(() => engine.ConvertAsync(new ConversionRequest { Input = Path.Combine(root, "tone.wav"), OutputDirectory = root,
                    Target = ConsoleTarget.PSP, Output = OutputKind.ATRAC, Bitrate = 13, Options = new CodecOptions() }, text => { }, CancellationToken.None).GetAwaiter().GetResult());
            });
            Run("Pre-cancelled conversion creates no output", delegate {
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    Throws<OperationCanceledException>(() => new ConversionEngine(new ToolRegistry(FixtureConfiguration())).ConvertAsync(new ConversionRequest(), text => { }, cts.Token).GetAwaiter().GetResult());
                }
            });
            Run("WinForms layout renders without desktop capture", delegate {
                Application.EnableVisualStyles();
                using (var form = new MainForm(new AppConfiguration(), Path.Combine(root, "ui-settings.json")))
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-12000, -12000); form.ShowInTaskbar = false;
                    form.Show(); form.PerformLayout(); Application.DoEvents(); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(root, "application-preview.png"));
                    Assert(form.Controls.Count == 1 && form.Width >= 730, "Layout");
                    form.AddFiles(new[] { Path.Combine(root,"tone.wav") });
                    form.PerformLayout(); Application.DoEvents();
                    var loadedQueue = (ListView)form.Controls.Find("AudioQueue",true).Single();
                    Assert(loadedQueue.Visible && loadedQueue.Items.Count == 1 && loadedQueue.Height > 30 && loadedQueue.SmallImageList.Handle != IntPtr.Zero,"Loaded file list or deferred icon handle failed");
                    form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));
                    image.Save(Path.Combine(root,"application-loaded-preview.png"));
                }
            });
            Run("Audio selection infers encode and decode routes", delegate {
                using (var form = new MainForm(new AppConfiguration(),Path.Combine(root,"ui-mode.json")))
                {
                    string encoded = Path.Combine(root,"ui-input.at3"); WriteSyntheticAtrac(encoded);
                    form.AddFiles(new[] {encoded});
                    var format = (ComboBox)form.Controls.Find("OutputFormat",true).Single();
                    Assert(format.SelectedIndex == 1 && format.Enabled,"Encoded input should select WAV decoding");
                }
                using (var form = new MainForm(new AppConfiguration(),Path.Combine(root,"ui-mode-2.json")))
                {
                    form.AddFiles(new[] {Path.Combine(root,"tone.wav")});
                    var format = (ComboBox)form.Controls.Find("OutputFormat",true).Single();
                    Assert(format.SelectedIndex == 0 && !format.Enabled,"PCM input should select console encoding");
                }
                foreach (string name in new[] {"psp","ps3","vita","ps4"})
                    Assert(typeof(MainForm).Assembly.GetManifestResourceStream("console."+name) != null,"Missing console image");
            });
            Run("Options render with general controls separated from advanced settings", delegate {
                using (var form = new OptionsForm(new AppConfiguration(),false,ConsoleProfile.For(ConsoleTarget.Vita),144,false))
                {
                    form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-12000,-12000); form.ShowInTaskbar = false; form.Show();
                    form.PerformLayout(); Application.DoEvents();
                    using (var image = new Bitmap(form.Width,form.Height))
                    { form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size)); image.Save(Path.Combine(root,"options-preview.png")); }
                    Assert(form.Bitrate == 144,"Default bitrate changed");
                }
            });
            Run("MP3 keeps PSP conversion actionable with an incomplete package", delegate {
                using (var form = new MainForm(new AppConfiguration(),Path.Combine(root,"psp-missing-encoder.json")))
                {
                    form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-12000,-12000); form.ShowInTaskbar = false; form.Show();
                    foreach (Control child in Descendants(form))
                    {
                        var device = child as ConsoleButton;
                        if (device != null && device.Profile.Target == ConsoleTarget.PSP) device.PerformClick();
                    }
                    form.AddFiles(new[] {Path.Combine(root,"silent.mp3")});
                    var format = (ComboBox)form.Controls.Find("OutputFormat",true).Single();
                    var action = (Button)form.Controls.Find("ConvertAudio",true).Single();
                    Assert(format.SelectedIndex == 0 && format.Text == "AT3","MP3 did not select PSP AT3 encoding");
                    Assert(action.Enabled,"Convert is disabled without explaining the missing package");
                    form.PerformLayout(); Application.DoEvents();
                    using (var image = new Bitmap(form.Width,form.Height))
                    { form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size)); image.Save(Path.Combine(root,"psp-setup-preview.png")); }
                }
            });
            Run("PSP Convert produces audio directly and retains the default bitrate", delegate {
                var config = FixtureConfiguration();
                using (var form = new MainForm(config,Path.Combine(root,"direct-psp.json")))
                {
                    ShowOffscreen(form);
                    Descendants(form).OfType<ConsoleButton>().Single(b => b.Profile.Target == ConsoleTarget.PSP).PerformClick();
                    form.AddFiles(new[] {Path.Combine(root,"silent.mp3")});
                    var action = (Button)form.Controls.Find("ConvertAudio",true).Single();
                    var audio = (ListView)form.Controls.Find("AudioQueue",true).Single();
                    action.PerformClick();
                    WaitUntil(() => audio.Items[0].SubItems[2].Text.StartsWith("Saved:",StringComparison.Ordinal),"PSP conversion did not finish directly");
                    WaveFile.Inspect(audio.Items[0].Name,true,".at3");
                    Assert(Path.GetFileName(audio.Items[0].Name).Contains("_132kbps"),"PSP default bitrate was lost");
                    string firstOutput = audio.Items[0].Name;
                    action.PerformClick(); WaitUntil(() => audio.Items[0].Name != firstOutput,"Second conversion did not finish");
                    Assert(!File.Exists(Path.Combine(root,"direct-psp.json")),"Conversion should not create tool approvals");
                }
            });
            Run("Incomplete portable Convert returns without opening an executable picker", delegate {
                string directory = Path.Combine(root,"missing-package"); Directory.CreateDirectory(directory);
                string source = Path.Combine(directory,"silent.mp3"); WriteSilentMp3(source);
                using (var form = new MainForm(new AppConfiguration(),Path.Combine(directory,"settings.json")))
                {
                    ShowOffscreen(form); form.AddFiles(new[] {source});
                    var action = (Button)form.Controls.Find("ConvertAudio",true).Single();
                    action.PerformClick(); Application.DoEvents();
                    var audio = (ListView)form.Controls.Find("AudioQueue",true).Single();
                    Assert(action.Enabled && audio.Items[0].SubItems[2].Text == "Missing files","The incomplete package lost the queued audio");
                    Assert(form.Controls.Find("ConversionStatus",true).Single().Text.Contains("complete portable ZIP"),"No package repair guidance");
                    Assert(!Directory.Exists(Path.Combine(directory,".psconverter-work")) && !File.Exists(Path.Combine(directory,"settings.json")),"Missing package created files");
                    Assert(Application.OpenForms.Count == 1,"Convert opened another form");
                }
            });
            Run("MP3 output converts directly using both configured tools", delegate {
                string directory = Path.Combine(root,"direct-mp3"); Directory.CreateDirectory(directory);
                string source = Path.Combine(directory,"input.at3"); WriteSyntheticAtrac(source);
                using (var form = new MainForm(FixtureConfiguration(),Path.Combine(directory,"settings.json")))
                {
                    ShowOffscreen(form); form.AddFiles(new[] {source});
                    ((ComboBox)form.Controls.Find("OutputFormat",true).Single()).SelectedIndex = 2;
                    ((Button)form.Controls.Find("ConvertAudio",true).Single()).PerformClick();
                    var audio = (ListView)form.Controls.Find("AudioQueue",true).Single();
                    WaitUntil(() => audio.Items[0].SubItems[2].Text.StartsWith("Saved:",StringComparison.Ordinal),"MP3 conversion did not finish");
                    WaveFile.ValidateMp3(audio.Items[0].Name);
                    Assert(!Directory.GetFiles(directory,"*.exe").Any(),"Conversion published an executable");
                }
            });
            bool sourceOnly=args.Length>2 && args[2]=="--source-only";
            if (!sourceOnly) {
            Run("Clean portable resolves every bundled encoder without settings", delegate {
                var registry = new ToolRegistry(new AppConfiguration(),args[1]);
                foreach (string name in ConsoleProfile.All.Select(p => p.Executable).Concat(new[] {"lame.exe"}))
                {
                    Assert(registry.Has(name),"Missing bundled encoder "+name);
                    using (var tool = registry.Open(name)) Assert(tool.Executable.StartsWith(Path.GetFullPath(args[1]),StringComparison.OrdinalIgnoreCase),"Encoder escaped package");
                }
                using (var form = new MainForm(new AppConfiguration(),Path.Combine(root,"bundle-ui.json"),registry))
                {
                    ShowOffscreen(form); form.AddFiles(new[] {Path.Combine(root,"silent.mp3")});
                    var audio = (ListView)form.Controls.Find("AudioQueue",true).Single();
                    Assert(audio.Items[0].SubItems[2].Text == "Ready","Clean package still asks for an encoder");
                }
            });
            Run("Portable relocation resolves relative codec paths without approvals", delegate {
                string relocated = Path.Combine(root,"relocated package"); CopyBundle(args[1],relocated);
                var registry = new ToolRegistry(new AppConfiguration(),relocated);
                foreach (string name in ConsoleProfile.All.Select(p => p.Executable).Concat(new[] {"lame.exe"}))
                    using (var tool = registry.Open(name)) Assert(tool.Executable.StartsWith(relocated+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Absolute codec path survived relocation");
            });
            Run("Bundled codec tampering is rejected before execution", delegate {
                string modified = Path.Combine(root,"modified-package"); CopyBundle(args[1],modified);
                File.AppendAllText(Path.Combine(modified,"codecs","PSVita_at9tool","PSVita_at9tool.exe"),"test mutation");
                Throws<InvalidOperationException>(() => { using (new ToolRegistry(new AppConfiguration(),modified).Open("PSVita_at9tool.exe")) {} });
            });
            }
            else Console.WriteLine("SKIP 3 real codec inventory/relocation/tampering checks in explicit source-only mode.");
            Run("Main window keeps conversion centered and long filenames below the toolbar", delegate {
                using (var form = new MainForm(FixtureConfiguration(),Path.Combine(root,"visual-layout.json")))
                {
                    ShowOffscreen(form); form.AddFiles(new[] {Path.Combine(root,"tone.wav")});
                    var queue = (ListView)form.Controls.Find("AudioQueue",true).Single();
                    queue.Items[0].Text = "A deliberately long generated track title used to check the file row layout and truncation.wav";
                    foreach (int width in new[] {730,760,1024})
                    {
                        form.Width=width; form.PerformLayout(); Application.DoEvents();
                        var action=(Button)form.Controls.Find("ConvertAudio",true).Single();
                        Assert(Math.Abs(action.Left*2+action.Width-action.Parent.ClientSize.Width)<=3,"Convert is not centered");
                        Assert(!Descendants(form).OfType<ThemedButton>().Any(b=>b.IconKind==UiIcon.Play || b.IconKind==UiIcon.Stop),"Playback controls are still shown");
                        var add=form.Controls.Find("AddAudio",true).Single();
                        int toolbarBottom=add.PointToScreen(new Point(0,add.Height)).Y;
                        Assert(queue.PointToScreen(Point.Empty).Y>=toolbarBottom+4,"The file list overlaps Add files");
                        Assert(queue.ClientSize.Width>queue.Columns[0].Width,"Status column is clipped");
                    }
                }
            });
            Run("Options have the application icon and explicit accessible control names", delegate {
                using (var form = new OptionsForm(new AppConfiguration(),false,ConsoleProfile.For(ConsoleTarget.PS3),128,false))
                {
                    ShowOffscreen(form);
                    Assert(form.Icon!=null && form.ShowIcon,"Options window icon is missing");
                    var bitrate=(ComboBox)form.Controls.Find("Bitrate",true).Single();
                    Assert(bitrate.Text=="128" && bitrate.AccessibleName=="Bitrate in kilobits per second","Bitrate changed or lacks an accessible name");
                }
            });
            Run("Changed executable and newly added dependency rejected", delegate {
                var config = FixtureConfiguration(); var registry = new ToolRegistry(config);
                string dependency = Path.Combine(fixtures, "unexpected.dll"); File.WriteAllText(dependency, "fixture");
                Throws<InvalidOperationException>(() => { using (registry.Open("PSP_at3tool.exe")) { } });
                // Approve the new file, then alter it; avoid modifying or removing any real external software.
                registry.Approve(ToolRegistry.Inspect("PSP_at3tool.exe", Path.Combine(fixtures, "PSP_at3tool.exe")));
                File.AppendAllText(dependency, "changed");
                Throws<InvalidOperationException>(() => { using (registry.Open("PSP_at3tool.exe")) { } });
            });
            Console.WriteLine("Passed: " + passed + "; failed: " + failed);
            File.WriteAllText(Path.Combine(root, "results.txt"), "Passed: " + passed + "; failed: " + failed + "\nAll ATRAC fixtures are synthetic. Real ATRAC algorithms were not tested.\n");
            return failed == 0 ? 0 : 1;
        }
        private static AppConfiguration FixtureConfiguration()
        {
            var config = new AppConfiguration();
            foreach (string name in ConsoleProfile.All.Select(p => p.Executable).Concat(new[] { "lame.exe" }))
                config.Tools.Add(ToolRegistry.Inspect(name, Path.Combine(fixtures, name)));
            return config;
        }
        private static void CopyBundle(string source, string destination)
        {
            using (var resource = typeof(ToolRegistry).Assembly.GetManifestResourceStream("codec.catalog"))
            {
                var catalog = (List<ApprovedTool>)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(List<ApprovedTool>)).ReadObject(resource);
                foreach (var tool in catalog)
                    foreach (var file in tool.Files)
                    {
                        string target = Path.Combine(destination,file.Path); Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(Path.Combine(source,file.Path),target);
                    }
            }
        }
        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in Descendants(child)) yield return descendant;
            }
        }
        private static void ShowOffscreen(Form form)
        {
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-12000,-12000); form.ShowInTaskbar = false;
            form.Show(); form.PerformLayout(); Application.DoEvents();
            Assert(SynchronizationContext.Current is WindowsFormsSynchronizationContext,"The UI test dispatch context was lost");
        }
        private static void WaitUntil(Func<bool> predicate,string message)
        {
            var time = Stopwatch.StartNew();
            while (!predicate() && time.Elapsed.TotalSeconds < 10) { Application.DoEvents(); Thread.Sleep(10); }
            Assert(predicate(),message);
        }
        private static void Run(string name, Action test)
        { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message); } }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        private static void WriteTone(string path, int rate, int channels)
        {
            using (var writer = new PcmWaveWriter(path, rate, channels))
            {
                byte[] samples = new byte[(rate / 4) * channels * 2];
                for (int i = 0; i < rate / 4; i++)
                {
                    short value = (short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 8000);
                    for (int c = 0; c < channels; c++) { int offset = (i * channels + c) * 2; samples[offset] = (byte)value; samples[offset + 1] = (byte)(value >> 8); }
                }
                writer.Write(samples, samples.Length); writer.Complete();
            }
        }
        private static void WriteFloat(string path)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + 4800 * 4); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((ushort)3); w.Write((ushort)1); w.Write(48000); w.Write(192000); w.Write((ushort)4); w.Write((ushort)32);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(4800 * 4);
                for (int i = 0; i < 4800; i++) w.Write((float)(Math.Sin(i * 2 * Math.PI * 440 / 48000) * 0.2));
            }
        }
        private static void WriteSilentMp3(string path)
        {
            using (var stream = File.Create(path))
            {
                var frame = new byte[417]; frame[0] = 0xff; frame[1] = 0xfb; frame[2] = 0x90;
                for (int i = 0; i < 64; i++) stream.Write(frame, 0, frame.Length);
            }
        }
        private static void WriteSyntheticAtrac(string path)
        {
            bool at9 = Path.GetExtension(path) == ".at9";
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(at9 ? 68 : 44); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(at9 ? 40 : 16); w.Write((ushort)(at9 ? 0xfffe : 0x270)); w.Write((ushort)2);
                w.Write(48000); w.Write(16000); w.Write((ushort)8); w.Write((ushort)0);
                if (at9) { w.Write((ushort)22); w.Write((ushort)0); w.Write(3); w.Write(new Guid("47e142d2-36ba-4d8d-88fc-61654f8c836c").ToByteArray()); }
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(8); w.Write(new byte[8]);
            }
        }
        private static int Fixture(string[] args)
        {
            switch (args[0])
            {
                case "--fixture-arguments": File.WriteAllLines(args[1], args.Skip(2)); return 0;
                case "--fixture-flood": string text = new string('x', 4096); for (int i = 0; i < 256; i++) { Console.Out.Write(text); Console.Error.Write(text); } return 0;
                case "--fixture-hang": Thread.Sleep(30000); return 0;
                case "--fixture-fail": return 42;
                case "--fixture-parent":
                    Thread.Sleep(400);
                    using (var child = Process.Start(new ProcessStartInfo { FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                        Arguments = "--fixture-hang", UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                    { File.WriteAllText(args[1], child.Id.ToString()); Thread.Sleep(30000); }
                    return 0;
                case "-e": if (args.Contains("13")) return 0; WriteSyntheticAtrac(args.Last()); return 0;
                case "-d": WriteTone(args.Last(), 48000, 2); return 0;
                case "-V2": WriteSilentMp3(args.Last()); return 0;
                default: return 90;
            }
        }
    }
}
