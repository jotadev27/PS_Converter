using System;
using System.IO;
using System.Text;

namespace PSConverter
{
    internal sealed class WaveInfo
    {
        public int Format, Channels, SampleRate, BitsPerSample;
        public long DataBytes;
        public long Frames { get { return DataBytes / (Channels * ((BitsPerSample + 7) / 8)); } }
    }

    internal static class WaveFile
    {
        private static string FourCC(BinaryReader reader) { return Encoding.ASCII.GetString(reader.ReadBytes(4)); }

        public static WaveInfo Inspect(string path, bool atrac, string extension)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 12 || FourCC(reader) != "RIFF") throw new InvalidDataException("The codec did not produce a valid RIFF audio file.");
                uint declared = reader.ReadUInt32();
                if (declared < 4 || (long)declared + 8 > stream.Length || FourCC(reader) != "WAVE")
                    throw new InvalidDataException("The audio container is truncated or invalid.");
                long containerEnd = (long)declared + 8;
                var info = new WaveInfo();
                Guid subformat = Guid.Empty;
                bool formatFound = false, dataFound = false;
                while (stream.Position + 8 <= containerEnd)
                {
                    string chunk = FourCC(reader); uint size = reader.ReadUInt32();
                    long next = stream.Position + size + (size & 1);
                    if (next > containerEnd) throw new InvalidDataException("An audio chunk is truncated.");
                    if (chunk == "fmt ")
                    {
                        if (size < 16) throw new InvalidDataException("Invalid audio format header.");
                        info.Format = reader.ReadUInt16(); info.Channels = reader.ReadUInt16();
                        info.SampleRate = reader.ReadInt32(); reader.ReadUInt32(); reader.ReadUInt16();
                        info.BitsPerSample = reader.ReadUInt16();
                        if (info.Format == 0xfffe)
                        {
                            if (size < 40) throw new InvalidDataException("Invalid extensible WAV header.");
                            ushort extra = reader.ReadUInt16();
                            if (extra < 22 || extra > size - 18) throw new InvalidDataException("Invalid extensible WAV format size.");
                            reader.ReadUInt16(); reader.ReadUInt32(); subformat = new Guid(reader.ReadBytes(16));
                        }
                        formatFound = true;
                    }
                    else if (chunk == "data") { info.DataBytes += size; dataFound = true; }
                    stream.Position = next;
                }
                if (!formatFound || !dataFound || info.DataBytes == 0 || info.Channels < 1 || info.SampleRate <= 0)
                    throw new InvalidDataException("The audio file has no valid format or audio data.");
                if (atrac)
                {
                    Guid at9 = new Guid("47e142d2-36ba-4d8d-88fc-61654f8c836c");
                    Guid at3plus = new Guid("e923aabf-cb58-4471-a119-fffa01e4ce62");
                    if (extension == ".at9" ? subformat != at9 : (info.Format != 0x270 && subformat != at3plus))
                        throw new InvalidDataException("The codec output does not match the requested ATRAC format.");
                }
                else
                {
                    if (info.Format == 0xfffe && (subformat == new Guid("00000001-0000-0010-8000-00aa00389b71") ||
                        subformat == new Guid("00000003-0000-0010-8000-00aa00389b71"))) info.Format = subformat.ToByteArray()[0];
                    if ((info.Format != 1 && info.Format != 3) || info.BitsPerSample < 8 || info.BitsPerSample > 64 ||
                        info.DataBytes % (info.Channels * ((info.BitsPerSample + 7) / 8)) != 0)
                        throw new InvalidDataException("The decoded file is not valid PCM audio.");
                }
                return info;
            }
        }

        public static void ValidateMp3(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 4) throw new InvalidDataException("The MP3 output is empty.");
                var head = new byte[10]; int read = stream.Read(head, 0, head.Length);
                long offset = 0;
                if (read >= 10 && head[0] == 'I' && head[1] == 'D' && head[2] == '3')
                {
                    for (int i = 6; i < 10; i++) if ((head[i] & 0x80) != 0) throw new InvalidDataException("Invalid MP3 tag size.");
                    offset = 10L + ((head[6] << 21) | (head[7] << 14) | (head[8] << 7) | head[9]);
                    if ((head[5] & 0x10) != 0) offset += 10;
                }
                stream.Position = offset;
                if (offset + 4 > stream.Length || stream.Read(head, 0, 4) != 4 || head[0] != 0xff || (head[1] & 0xe0) != 0xe0 ||
                    ((head[1] >> 3) & 3) == 1 || ((head[1] >> 1) & 3) != 1 ||
                    (head[2] >> 4) == 0 || (head[2] >> 4) == 15 || ((head[2] >> 2) & 3) == 3)
                    throw new InvalidDataException("The encoder did not produce a valid MP3 frame header.");
            }
        }
    }

    internal sealed class PcmWaveWriter : IDisposable
    {
        private readonly FileStream stream;
        private readonly BinaryWriter writer;
        private readonly int channels;
        private long bytes;
        public PcmWaveWriter(string path, int sampleRate, int channels)
        {
            this.channels = channels;
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(0U); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16U); writer.Write((ushort)1); writer.Write((ushort)channels); writer.Write(sampleRate);
            writer.Write(checked(sampleRate * channels * 2)); writer.Write((ushort)(channels * 2)); writer.Write((ushort)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(0U);
        }
        public void Write(byte[] buffer, int count)
        {
            if (bytes + count > uint.MaxValue - 36L)
                throw new InvalidOperationException("This audio exceeds the RIFF WAV size supported by the legacy encoders. Split the track before encoding.");
            stream.Write(buffer, 0, count); bytes += count;
        }
        public void Complete()
        {
            if (bytes == 0 || bytes % (channels * 2) != 0) throw new InvalidDataException("No complete PCM sample frames were decoded.");
            writer.Flush(); stream.Position = 4; writer.Write((uint)(bytes + 36)); stream.Position = 40; writer.Write((uint)bytes);
            writer.Flush();
        }
        public void Dispose() { writer.Dispose(); }
    }
}
