using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PSConverter
{
    internal static class CodecArguments
    {
        private static string Number(int value) { return value.ToString(CultureInfo.InvariantCulture); }

        public static void Validate(ConsoleProfile profile, CodecOptions options, bool encode)
        {
            if (options == null) throw new ArgumentException("Codec options are required.");
            if (options.TimeoutSeconds < 0 || options.Repeats < 1)
                throw new ArgumentException("Repeat count must be positive and timeout cannot be negative.");
            if (!Enum.IsDefined(typeof(LoopKind), options.Loop) || !Enum.IsDefined(typeof(PcmKind), options.Pcm) ||
                !Enum.IsDefined(typeof(SuperframeKind), options.Superframe))
                throw new ArgumentException("Invalid codec option.");
            if (!encode) return;
            if (options.Loop == LoopKind.Custom && (options.LoopStart < 0 ||
                (long)options.LoopEnd - options.LoopStart < (profile.IsAt9 ? 1 : 6143)))
                throw new ArgumentException(profile.IsAt9 ? "Loop end must follow loop start." : "AT3 loops need at least 6143 sample frames.");
            if (options.Loop == LoopKind.DefaultWholeTrack && profile.Target != ConsoleTarget.PS4)
                throw new ArgumentException("Default whole-track metadata is specific to PS4.");
            if (!profile.IsAt9 && (options.SampleRate != 0 || !string.IsNullOrEmpty(options.LoopList) ||
                options.Superframe != SuperframeKind.Automatic || options.Dual || options.Bands != 0 ||
                options.IntensityBand != -2 || options.Gradient != -1))
                throw new ArgumentException("The selected expert options require an AT9 console.");
            if (options.SampleRate < 0)
                throw new ArgumentException("Sample rate cannot be negative. Windows and the selected codec validate supported rates.");
            if (options.Channels < 0 || options.Channels > 2)
                throw new ArgumentException("Input channels must be 0 (preserve), 1 (mono) or 2 (stereo).");
            if ((options.Bands != 0 && (options.Bands < 3 || options.Bands > 18)) ||
                options.IntensityBand < -2 || options.IntensityBand > 18 || options.Gradient < -1 || options.Gradient > 4)
                throw new ArgumentException("Expert band or gradient settings are outside the codec range.");
            if (profile.Target != ConsoleTarget.PS4 && (options.WideBand || options.BandExtension || options.LowCut))
                throw new ArgumentException("Wide band, band extension and LFE low cut require PS4.");
            if (options.BandExtension && (options.WideBand || (options.SampleRate != 0 && options.SampleRate != 48000) ||
                (options.Bands != 0 && (options.Bands < 5 || options.Bands > 10))))
                throw new ArgumentException("Band extension requires 48 kHz, 5 to 10 bands and wide band disabled.");
            if (!string.IsNullOrWhiteSpace(options.LoopList))
            {
                options.LoopList = PathSafety.LocalPath(options.LoopList);
                if (!File.Exists(options.LoopList)) throw new FileNotFoundException("Loop list file was not found.");
                if (options.Loop == LoopKind.Custom) throw new ArgumentException("Choose either a loop list or a custom loop.");
            }
        }

        public static List<string> Encode(ConsoleProfile profile, int bitrate, CodecOptions o, string input, string output)
        {
            Validate(profile, o, true);
            if (bitrate <= 0) throw new ArgumentException("Bitrate must be positive; the encoder validates supported values.");
            var args = new List<string> { "-e", "-br", Number(bitrate) };
            switch (o.Loop)
            {
                case LoopKind.WholeTrack: args.Add("-wholeloop"); break;
                case LoopKind.Custom: args.AddRange(new[] { "-loop", Number(o.LoopStart), Number(o.LoopEnd) }); break;
                case LoopKind.DefaultWholeTrack: args.Add("-defaultWL"); break;
            }
            if (o.SampleRate != 0) args.AddRange(new[] { "-fs", Number(o.SampleRate) });
            if (!string.IsNullOrWhiteSpace(o.LoopList)) args.AddRange(new[] { "-looplist", o.LoopList });
            if (o.Superframe != SuperframeKind.Automatic) args.Add(o.Superframe == SuperframeKind.Enabled ? "-supframeon" : "-supframeoff");
            if (o.Dual) args.Add("-dual");
            if (o.Bands != 0) args.AddRange(new[] { "-nbands", Number(o.Bands) });
            if (o.IntensityBand != -2) args.AddRange(new[] { "-isband", Number(o.IntensityBand) });
            if (o.Gradient != -1) args.AddRange(new[] { "-gradmode", Number(o.Gradient) });
            if (o.WideBand) args.Add("-wband");
            if (o.BandExtension) args.Add("-bex");
            if (o.LowCut) args.Add("-slc");
            args.Add(input); args.Add(output); return args;
        }

        public static List<string> Decode(ConsoleProfile profile, CodecOptions o, string input, string output)
        {
            Validate(profile, o, false);
            var args = new List<string> { "-d", "-repeat", Number(o.Repeats) };
            if (profile.Target == ConsoleTarget.PS4)
            {
                args.Add(o.Pcm == PcmKind.Int24 ? "-int24" : o.Pcm == PcmKind.Float ? "-float" : "-int16");
                if (o.ExtensibleWave) args.Add("-wext");
            }
            else if (o.Pcm != PcmKind.Int16 || o.ExtensibleWave)
                throw new ArgumentException("The selected WAV output options require PS4.");
            args.Add(input); args.Add(output); return args;
        }

        public static string Quote(string argument)
        {
            if (argument == null || argument.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("Invalid process argument.");
            // Windows argument parsing doubles backslashes before quotes and before the closing quote.
            var result = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c); slashes = 0;
            }
            result.Append('\\', slashes * 2); result.Append('"'); return result.ToString();
        }
    }
}
