using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace PSConverter
{
    internal static class WindowsAudio
    {
        private const uint AudioStream = 0xfffffffd;
        private static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid Audio = new Guid("73647561-0000-0010-8000-00aa00389b71");
        private static readonly Guid Pcm = new Guid("00000001-0000-0010-8000-00aa00389b71");
        private static readonly Guid Rate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        private static readonly Guid Channels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        private static readonly Guid Bits = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        private static readonly Guid Alignment = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        private static readonly Guid BytesPerSecond = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");

        public static void Normalize(string input, string output, int rate, int channels, CancellationToken token)
        {
            input = PathSafety.LocalPath(input);
            token.ThrowIfCancellationRequested();
            Check(MFStartup(0x20070, 0));
            IntPtr reader = IntPtr.Zero, mediaType = IntPtr.Zero, actualType = IntPtr.Zero;
            try
            {
                Check(MFCreateSourceReaderFromURL(input, IntPtr.Zero, out reader));
                Check(Method<SelectStream>(reader, 4)(reader, 0xfffffffe, false));
                Check(Method<SelectStream>(reader, 4)(reader, AudioStream, true));
                Check(MFCreateMediaType(out mediaType));
                SetGuid(mediaType, MajorType, Audio); SetGuid(mediaType, Subtype, Pcm);
                Check(Method<SetMediaType>(reader, 7)(reader, AudioStream, IntPtr.Zero, mediaType));
                Check(Method<GetMediaType>(reader, 6)(reader, AudioStream, out actualType));
                if (channels == 0) channels = checked((int)GetUInt(actualType, Channels));
                if (channels < 1 || channels > 8) throw new InvalidOperationException("The source channel layout is not supported by these codecs.");
                SetUInt(mediaType, Rate, (uint)rate); SetUInt(mediaType, Channels, (uint)channels);
                SetUInt(mediaType, Bits, 16); SetUInt(mediaType, Alignment, (uint)(channels * 2));
                SetUInt(mediaType, BytesPerSecond, checked((uint)(rate * channels * 2)));
                Check(Method<SetMediaType>(reader, 7)(reader, AudioStream, IntPtr.Zero, mediaType));
                Release(ref actualType);
                Check(Method<GetMediaType>(reader, 6)(reader, AudioStream, out actualType));
                if (GetUInt(actualType, Rate) != rate || GetUInt(actualType, Channels) != channels || GetUInt(actualType, Bits) != 16)
                    throw new InvalidOperationException("Windows could not negotiate the required PCM format.");

                using (var writer = new PcmWaveWriter(output, rate, channels))
                {
                    byte[] copy = new byte[65536];
                    for (;;)
                    {
                        token.ThrowIfCancellationRequested();
                        uint actualStream, flags; long timestamp; IntPtr sample = IntPtr.Zero;
                        Check(Method<ReadSample>(reader, 9)(reader, AudioStream, 0, out actualStream, out flags, out timestamp, out sample));
                        try
                        {
                            if ((flags & 1) != 0) throw new InvalidOperationException("Windows reported an audio decoding error.");
                            if ((flags & 0x30) != 0) throw new InvalidOperationException("The audio format changed while decoding.");
                            if (sample != IntPtr.Zero)
                            {
                                IntPtr buffer = IntPtr.Zero;
                                try
                                {
                                    Check(Method<GetBuffer>(sample, 41)(sample, out buffer));
                                    IntPtr data; uint maximum, length;
                                    Check(Method<LockBuffer>(buffer, 3)(buffer, out data, out maximum, out length));
                                    try
                                    {
                                        if (length > maximum) throw new InvalidOperationException("Windows returned an invalid audio buffer.");
                                        for (uint offset = 0; offset < length;)
                                        {
                                            token.ThrowIfCancellationRequested();
                                            int count = (int)Math.Min((uint)copy.Length, length - offset);
                                            Marshal.Copy(new IntPtr(data.ToInt64() + offset), copy, 0, count);
                                            writer.Write(copy, count); offset += (uint)count;
                                        }
                                    }
                                    finally { Check(Method<SimpleMethod>(buffer, 4)(buffer)); }
                                }
                                finally { Release(ref buffer); }
                            }
                            if ((flags & 2) != 0) break;
                        }
                        finally { Release(ref sample); }
                    }
                    writer.Complete();
                }
            }
            catch (DllNotFoundException) { throw new InvalidOperationException("Windows Media Foundation is required. Windows N editions need the Media Feature Pack."); }
            finally { Release(ref actualType); Release(ref mediaType); Release(ref reader); MFShutdown(); }
        }

        // These slots follow the published COM interface ABI. Keep dispatch isolated from conversion logic.
        private static T Method<T>(IntPtr instance, int slot) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(instance);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, slot * IntPtr.Size), typeof(T)) as T;
        }
        private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
        private static void Release(ref IntPtr value) { if (value != IntPtr.Zero) { Marshal.Release(value); value = IntPtr.Zero; } }
        private static void SetGuid(IntPtr type, Guid key, Guid value) { Check(Method<SetGuidValue>(type, 24)(type, ref key, ref value)); }
        private static void SetUInt(IntPtr type, Guid key, uint value) { Check(Method<SetUIntValue>(type, 21)(type, ref key, value)); }
        private static uint GetUInt(IntPtr type, Guid key) { uint value; Check(Method<GetUIntValue>(type, 7)(type, ref key, out value)); return value; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuidValue(IntPtr self, ref Guid key, ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUIntValue(IntPtr self, ref Guid key, uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetUIntValue(IntPtr self, ref Guid key, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SelectStream(IntPtr self, uint index, [MarshalAs(UnmanagedType.Bool)] bool selected);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetMediaType(IntPtr self, uint index, IntPtr reserved, IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetMediaType(IntPtr self, uint index, out IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReadSample(IntPtr self, uint index, uint control, out uint actual, out uint flags, out long timestamp, out IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBuffer(IntPtr self, out IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LockBuffer(IntPtr self, out IntPtr data, out uint maximum, out uint length);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SimpleMethod(IntPtr self);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMediaType(out IntPtr mediaType);
        [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);
    }
}
