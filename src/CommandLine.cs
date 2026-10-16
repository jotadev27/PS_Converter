using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PSConverter
{
    internal static class CommandLine
    {
        public static async Task<int> RunAsync(string[] args)
        {
            var inputs = new List<string>(); string directory = null, configPath = null, approveDirectory = null;
            var request = new ConversionRequest { Target = ConsoleTarget.Vita, Output = OutputKind.ATRAC };
            int? bitrate = null;
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (key == "--help") { Help(); return 0; }
                if (key == "--convert") continue;
                if (i + 1 >= args.Length) throw new ArgumentException("Missing value for " + key + ".");
                string value = args[++i];
                switch (key)
                {
                    case "--input": inputs.Add(value); break;
                    case "--output-dir": directory = value; break;
                    case "--config": configPath = value; break;
                    case "--approve-tools-directory": approveDirectory = value; break;
                    case "--platform":
                        if (!Enum.TryParse<ConsoleTarget>(value, true, out request.Target)) throw new ArgumentException("Platform must be PSP, PS3, Vita or PS4.");
                        break;
                    case "--format":
                        if (!Enum.TryParse<OutputKind>(value, true, out request.Output)) throw new ArgumentException("Format must be ATRAC, WAV or MP3.");
                        break;
                    case "--bitrate": int parsed; if (!int.TryParse(value, out parsed) || parsed <= 0) throw new ArgumentException("Bitrate must be positive."); bitrate = parsed; break;
                    default: throw new ArgumentException("Unknown option: " + key);
                }
            }
            ConsoleProfile profile = ConsoleProfile.For(request.Target);
            if (configPath == null) configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "settings.json");
            AppConfiguration config = ConfigurationStore.Load(configPath);
            var registry = new ToolRegistry(config);
            if (approveDirectory != null)
            {
                string root = PathSafety.LocalPath(approveDirectory);
                foreach (string name in ConsoleProfile.All.Select(p => p.Executable).Concat(new[] { "lame.exe" }))
                {
                    string candidate = Path.Combine(root, name);
                    if (File.Exists(candidate)) { registry.Approve(ToolRegistry.Inspect(name, candidate)); Console.WriteLine("Approved " + name + "."); }
                }
                ConfigurationStore.Save(configPath, config);
            }
            if (inputs.Count == 0)
            {
                if (approveDirectory != null) return 0;
                throw new ArgumentException("Provide at least one --input.");
            }
            if (directory == null) throw new ArgumentException("Provide --output-dir.");
            request.OutputDirectory = directory; request.Bitrate = bitrate ?? profile.DefaultBitrate; request.Options = config.Options;
            int failed = 0;
            using (var cancellation = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler cancel = delegate(object sender, ConsoleCancelEventArgs e) { e.Cancel = true; cancellation.Cancel(); };
                Console.CancelKeyPress += cancel;
                try
                {
                    var engine = new ConversionEngine(registry);
                    for (int i = 0; i < inputs.Count; i++)
                    {
                        request.Input = inputs[i];
                        try { await engine.ConvertAsync(request, text => Console.WriteLine(text), cancellation.Token); Console.WriteLine("Converted input " + (i + 1) + "."); }
                        catch (OperationCanceledException) { Console.WriteLine("Cancelled."); return 2; }
                        catch (Exception ex) { Console.WriteLine("Input " + (i + 1) + " failed: " + ex.Message); failed++; }
                    }
                }
                finally { Console.CancelKeyPress -= cancel; }
            }
            return failed == 0 ? 0 : 1;
        }
        private static void Help()
        {
            Console.WriteLine("PS_converter --convert --input <file> [--input <file> ...] --output-dir <folder>");
            Console.WriteLine("  --platform PSP|PS3|Vita|PS4   --format ATRAC|WAV|MP3   --bitrate <kbps>");
            Console.WriteLine("  --config <settings.json>     --approve-tools-directory <dedicated codec folder>");
            Console.WriteLine("Explicit approval pins the tools and adjacent dependencies. Obtain codecs legally and review their source first.");
        }
    }
}
