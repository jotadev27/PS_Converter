using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace PSConverter
{
    public enum ConsoleTarget { PSP, PS3, Vita, PS4 }
    public enum OutputKind { ATRAC, WAV, MP3 }
    public enum LoopKind { WholeTrack, None, Custom, DefaultWholeTrack }
    public enum SuperframeKind { Automatic, Enabled, Disabled }
    public enum PcmKind { Int16, Int24, Float }

    public sealed class ConsoleProfile
    {
        public readonly ConsoleTarget Target;
        public readonly string Name, Extension, Executable;
        public readonly int SampleRate, DefaultBitrate;
        public readonly int[] Bitrates;
        public bool IsAt9 { get { return Extension == ".at9"; } }

        private ConsoleProfile(ConsoleTarget target, string name, string extension, string executable,
            int rate, int bitrate, int[] bitrates)
        {
            Target = target; Name = name; Extension = extension; Executable = executable;
            SampleRate = rate; DefaultBitrate = bitrate; Bitrates = bitrates;
        }

        public static readonly ConsoleProfile[] All = {
            new ConsoleProfile(ConsoleTarget.PSP, "PSP", ".at3", "PSP_at3tool.exe", 44100, 132,
                new[] {32,48,52,64,66,96,105,128,132,160,192,256,320,352}),
            new ConsoleProfile(ConsoleTarget.PS3, "PS3", ".at3", "PS3_at3tool.exe", 48000, 128,
                new[] {32,48,57,64,72,96,114,128,144,160,192,256,320,384,512,768}),
            new ConsoleProfile(ConsoleTarget.Vita, "PS Vita", ".at9", "PSVita_at9tool.exe", 48000, 144,
                new[] {36,48,60,72,84,96,120,144,168,192}),
            new ConsoleProfile(ConsoleTarget.PS4, "PS4", ".at9", "PS4_at9tool.exe", 48000, 144,
                new[] {36,48,60,72,84,96,120,144,168,192,240,288,300,336,360,384,420,480,504,672})
        };
        public static ConsoleProfile For(ConsoleTarget target)
        {
            foreach (ConsoleProfile profile in All) if (profile.Target == target) return profile;
            throw new ArgumentOutOfRangeException("target");
        }
    }

    [DataContract]
    public sealed class CodecOptions
    {
        [DataMember, Category("1. Loop"), DisplayName("Loop mode"), Description("Custom positions use PCM sample frames, not milliseconds.")]
        public LoopKind Loop { get; set; }
        [DataMember, Category("1. Loop"), DisplayName("Start sample")]
        public int LoopStart { get; set; }
        [DataMember, Category("1. Loop"), DisplayName("End sample")]
        public int LoopEnd { get; set; }
        [DataMember, Category("2. Decode"), DisplayName("Loop repeats"), Description("Positive repeat count. No application-imposed cap.")]
        public int Repeats { get; set; }
        [DataMember, Category("2. Decode"), DisplayName("PS4 PCM format")]
        public PcmKind Pcm { get; set; }
        [DataMember, Category("2. Decode"), DisplayName("PS4 extensible WAV header")]
        public bool ExtensibleWave { get; set; }
        [DataMember, Category("3. AT9 expert"), DisplayName("Sample rate override"), Description("0 uses the console default; otherwise use a supported AT9 sample rate.")]
        public int SampleRate { get; set; }
        [DataMember, Category("3. AT9 expert"), DisplayName("Input channels"), Description("0 preserves the source, 1 selects mono, 2 selects stereo. PSP always uses stereo.")]
        public int Channels { get; set; }
        [DataMember, Category("3. AT9 expert"), DisplayName("Loop list file"), Description("Optional local text file. Do not combine it with a custom loop.")]
        public string LoopList { get; set; }
        [DataMember, Category("3. AT9 expert")]
        public SuperframeKind Superframe { get; set; }
        [DataMember, Category("3. AT9 expert"), DisplayName("Dual mode")]
        public bool Dual { get; set; }
        [DataMember, Category("3. AT9 expert"), DisplayName("Quantized bands"), Description("0 uses codec defaults; otherwise 3 to 18.")]
        public int Bands { get; set; }
        [DataMember, Category("3. AT9 expert"), DisplayName("Intensity band"), Description("-2 uses codec defaults; otherwise -1 to 18.")]
        public int IntensityBand { get; set; }
        [DataMember, Category("3. AT9 expert"), DisplayName("Gradient mode"), Description("-1 uses codec defaults; otherwise 0 to 4.")]
        public int Gradient { get; set; }
        [DataMember, Category("4. PS4 expert"), DisplayName("Wide band")]
        public bool WideBand { get; set; }
        [DataMember, Category("4. PS4 expert"), DisplayName("Band extension")]
        public bool BandExtension { get; set; }
        [DataMember, Category("4. PS4 expert"), DisplayName("LFE low cut")]
        public bool LowCut { get; set; }
        [DataMember, Category("5. Process"), DisplayName("Timeout seconds"), Description("0 disables the deadline. Cancel remains available.")]
        public int TimeoutSeconds { get; set; }

        public CodecOptions()
        { Loop = LoopKind.WholeTrack; Repeats = 1; LoopList = ""; IntensityBand = -2; Gradient = -1; }
        public CodecOptions Copy() { return (CodecOptions)MemberwiseClone(); }
    }

    public sealed class ConversionRequest
    {
        public string Input, OutputDirectory;
        public ConsoleTarget Target;
        public OutputKind Output;
        public int Bitrate;
        public CodecOptions Options;
    }

    public sealed class ConversionResult
    {
        public string OutputPath;
        public string Workspace;
    }
}
