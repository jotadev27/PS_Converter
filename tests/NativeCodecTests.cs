using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PSConverter
{
    // Explicit native integration suite. It runs the inspected packaged binaries on generated audio only.
    internal static class NativeCodecTests
    {
        [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
        [STAThread]
        private static int Main(string[] args)
        {
            SetErrorMode(0x8003);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Control.CheckForIllegalCrossThreadCalls = true;
            WindowsFormsSynchronizationContext.AutoInstall = false;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            string package = Path.GetFullPath(args[0]);
            string root = Path.Combine(Path.GetFullPath(args[1]),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string tone = Path.Combine(root,"generated-tone.wav"), mp3 = Path.Combine(root,"generated-silence.mp3");
            WriteTone(tone); WriteSilentMp3(mp3);
            var report = new StringBuilder(); int failed = 0;
            Action<string> record = message => { Console.WriteLine(message); report.AppendLine(message); };
            var config = new AppConfiguration(); var registry = new ToolRegistry(config,package);
            var engine = new ConversionEngine(registry);
            foreach (ConsoleProfile profile in ConsoleProfile.All)
            {
                try
                {
                    string output = Path.Combine(root,profile.Target.ToString()); Directory.CreateDirectory(output);
                    foreach (string source in new[] {tone,mp3})
                    {
                        var encoded = Convert(engine,source,output,profile,OutputKind.ATRAC);
                        if (Path.GetExtension(encoded.OutputPath) != profile.Extension) throw new Exception("Wrong output extension");
                        var decoded = Convert(engine,encoded.OutputPath,output,profile,OutputKind.WAV);
                        WaveInfo wave = WaveFile.Inspect(decoded.OutputPath,false,".wav");
                        if (wave.SampleRate != profile.SampleRate || wave.Frames < profile.SampleRate) throw new Exception("Decoded duration/rate mismatch");
                        if (source == tone)
                        {
                            if (Math.Abs(wave.Frames / (double)wave.SampleRate - 2) > 0.15) throw new Exception("Tone duration changed");
                            string pcm = Path.Combine(output,"decoded-pcm16.wav");
                            WindowsAudio.Normalize(decoded.OutputPath,pcm,profile.SampleRate,2,CancellationToken.None);
                            if (RootMeanSquare(pcm) < 0.1) throw new Exception("Tone became silent");
                        }
                        var encodedMp3 = Convert(engine,encoded.OutputPath,output,profile,OutputKind.MP3);
                        WaveFile.ValidateMp3(encodedMp3.OutputPath);
                        record("PASS "+profile.Name+": "+Path.GetExtension(source)+" -> "+profile.Extension+" -> WAV / MP3");
                    }
                    if (Directory.GetFiles(output,"*.exe").Length != 0) throw new Exception("Conversion published an executable");
                }
                catch (Exception ex) { failed++; record("FAIL "+profile.Name+": "+ex.Message); }
            }
            try
            {
                using (var form = new MainForm(config,Path.Combine(root,"unused-settings.json"),registry))
                {
                    form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-12000,-12000); form.ShowInTaskbar = false;
                    form.Show(); form.PerformLayout(); Application.DoEvents(); form.AddFiles(new[] {mp3});
                    var action = (Button)form.Controls.Find("ConvertAudio",true).Single();
                    var queue = (ListView)form.Controls.Find("AudioQueue",true).Single();
                    action.PerformClick(); var time = Stopwatch.StartNew();
                    while (queue.Items[0].SubItems[2].Text == "Converting..." && time.Elapsed.TotalSeconds < 20)
                    { Application.DoEvents(); Thread.Sleep(10); }
                    if (!queue.Items[0].SubItems[2].Text.StartsWith("Saved:",StringComparison.Ordinal)) throw new Exception(queue.Items[0].SubItems[2].Text);
                    WaveFile.Inspect(queue.Items[0].Name,true,".at9");
                    if (Application.OpenForms.Count != 1 || File.Exists(Path.Combine(root,"unused-settings.json"))) throw new Exception("Conversion opened setup or saved approvals");
                    record("PASS Vita Convert button: clean settings, generated MP3, real AT9 output, no executable picker");
                }
            }
            catch (Exception ex) { failed++; record("FAIL Vita Convert button: "+ex.Message); }
            record("Failing platform/workflow groups: "+failed);
            File.WriteAllText(Path.Combine(root,"native-results.txt"),report.ToString());
            Console.WriteLine("Results: "+root);
            return failed == 0 ? 0 : 1;
        }
        private static ConversionResult Convert(ConversionEngine engine,string source,string output,ConsoleProfile profile,OutputKind kind)
        {
            return engine.ConvertAsync(new ConversionRequest {Input=source,OutputDirectory=output,Target=profile.Target,Output=kind,
                Bitrate=profile.DefaultBitrate,Options=new CodecOptions {TimeoutSeconds=15}},message=>{},CancellationToken.None).GetAwaiter().GetResult();
        }
        private static void WriteTone(string path)
        {
            using (var writer = new PcmWaveWriter(path,48000,2))
            {
                var buffer = new byte[48000*2*2*2];
                for (int frame=0; frame<96000; frame++)
                    for (int channel=0; channel<2; channel++)
                    {
                        short value=(short)(Math.Sin(frame*2*Math.PI*(channel==0?440:660)/48000)*10000);
                        int offset=(frame*2+channel)*2; buffer[offset]=(byte)value; buffer[offset+1]=(byte)(value>>8);
                    }
                writer.Write(buffer,buffer.Length); writer.Complete();
            }
        }
        private static void WriteSilentMp3(string path)
        {
            using (var stream=File.Create(path))
            {
                var frame=new byte[417]; frame[0]=0xff; frame[1]=0xfb; frame[2]=0x90;
                for (int i=0;i<64;i++) stream.Write(frame,0,frame.Length);
            }
        }
        private static double RootMeanSquare(string path)
        {
            using (var reader=new BinaryReader(File.OpenRead(path)))
            {
                reader.BaseStream.Position=12;
                while (reader.BaseStream.Position+8<=reader.BaseStream.Length)
                {
                    string id=Encoding.ASCII.GetString(reader.ReadBytes(4)); uint length=reader.ReadUInt32();
                    if (id=="data")
                    {
                        double sum=0; long count=length/2;
                        for (long i=0;i<count;i++) { double value=reader.ReadInt16()/32768.0; sum+=value*value; }
                        return Math.Sqrt(sum/count);
                    }
                    reader.BaseStream.Position+=length+(length&1);
                }
            }
            throw new InvalidDataException("No PCM data");
        }
    }
}
