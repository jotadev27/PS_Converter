using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PSConverter
{
    internal sealed class ConversionEngine
    {
        private readonly ToolRegistry registry;
        private readonly ProcessRunner runner = new ProcessRunner();
        public ConversionEngine(ToolRegistry registry) { this.registry = registry; }

        public async Task<ConversionResult> ConvertAsync(ConversionRequest request, Action<string> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string input = PathSafety.LocalPath(request.Input);
            string directory = PathSafety.LocalPath(request.OutputDirectory);
            if (!File.Exists(input)) throw new FileNotFoundException("The input file was not found.");
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Choose an existing output folder.");
            ConsoleProfile profile = ConsoleProfile.For(request.Target);
            string inputExtension = Path.GetExtension(input).ToLowerInvariant();
            bool encode = request.Output == OutputKind.ATRAC;
            if (encode ? inputExtension != ".wav" && inputExtension != ".mp3" : inputExtension != profile.Extension)
                throw new ArgumentException(encode ? "ATRAC encoding accepts WAV or MP3 inputs." : "Select the console that matches this AT3 or AT9 input.");
            if (!Enum.IsDefined(typeof(OutputKind), request.Output)) throw new ArgumentException("Invalid output format.");
            CodecOptions options = (request.Options ?? new CodecOptions()).Copy();
            CodecArguments.Validate(profile, options, encode);
            if (encode && request.Bitrate <= 0) throw new ArgumentException("Bitrate must be positive.");
            using (ToolLease codec = registry.Open(profile.Executable))
            using (ToolLease mp3 = request.Output == OutputKind.MP3 ? registry.Open("lame.exe") : null)
            {
                string workspace = PathSafety.NewWorkspace(directory);
                string localInput = Path.Combine(workspace, "source" + inputExtension);
                progress("Reading input");
                // Snapshot to a fixed anonymous name; old tools must never see a source path they could overwrite.
                await CopyAsync(input, localInput, token).ConfigureAwait(false);
                string wave = Path.Combine(workspace, "audio.wav");
                string extension = encode ? profile.Extension : request.Output == OutputKind.MP3 ? ".mp3" : ".wav";
                string staged = Path.Combine(workspace, "result" + extension);
                if (encode)
                {
                    progress("Preparing PCM audio");
                    int rate = options.SampleRate == 0 ? profile.SampleRate : options.SampleRate;
                    int channels = profile.Target == ConsoleTarget.PSP ? 2 : options.Channels;
                    await Task.Run(() => WindowsAudio.Normalize(localInput, wave, rate, channels, token), token).ConfigureAwait(false);
                    WaveInfo normalized = WaveFile.Inspect(wave, false, ".wav");
                    if (options.Loop == LoopKind.Custom && options.LoopEnd >= normalized.Frames)
                        throw new ArgumentException("Loop end exceeds the decoded track's sample count.");
                    if (!string.IsNullOrWhiteSpace(options.LoopList))
                    {
                        string localList = Path.Combine(workspace, "loops.txt");
                        await CopyAsync(options.LoopList, localList, token).ConfigureAwait(false); options.LoopList = localList;
                    }
                    progress("Encoding " + profile.Extension.Substring(1).ToUpperInvariant());
                    await runner.RunAsync(codec, CodecArguments.Encode(profile, request.Bitrate, options, wave, staged),
                        workspace, options.TimeoutSeconds, token).ConfigureAwait(false);
                    WaveFile.Inspect(staged, true, extension);
                }
                else
                {
                    WaveFile.Inspect(localInput, true, inputExtension);
                    progress("Decoding ATRAC audio");
                    await runner.RunAsync(codec, CodecArguments.Decode(profile, options, localInput, wave),
                        workspace, options.TimeoutSeconds, token).ConfigureAwait(false);
                    WaveFile.Inspect(wave, false, ".wav");
                    if (request.Output == OutputKind.MP3)
                    {
                        progress("Encoding MP3");
                        string pcm16 = Path.Combine(workspace, "mp3-input.wav");
                        await Task.Run(() => WindowsAudio.Normalize(wave, pcm16, profile.SampleRate, 0, token), token).ConfigureAwait(false);
                        await runner.RunAsync(mp3, new[] { "-V2", pcm16, staged }, workspace, options.TimeoutSeconds, token).ConfigureAwait(false);
                        WaveFile.ValidateMp3(staged);
                    }
                    else File.Move(wave, staged);
                }
                token.ThrowIfCancellationRequested();
                progress("Saving output");
                string name = Path.GetFileNameWithoutExtension(input);
                if (encode) name += "_" + request.Bitrate + "kbps";
                string output = PathSafety.Publish(staged, directory, name, extension);
                return new ConversionResult { OutputPath = output, Workspace = workspace };
            }
        }

        private static async Task CopyAsync(string source, string target, CancellationToken token)
        {
            using (var input = new FileStream(PathSafety.LocalPath(source), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; int count;
                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                    await output.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
            }
        }
    }
}
