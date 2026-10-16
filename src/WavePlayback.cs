using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PSConverter
{
    internal sealed class WavePlayback : IDisposable
    {
        private readonly string alias = "pspreview" + Guid.NewGuid().ToString("N");
        private bool opened;
        public void Play(string path)
        {
            Stop();
            // MCI waveaudio streams from disk instead of caching the entire decoded track in managed memory.
            Check(mciSendString("open \"" + PathSafety.LocalPath(path) + "\" type waveaudio alias " + alias, null, 0, IntPtr.Zero));
            opened = true;
            try { Check(mciSendString("play " + alias, null, 0, IntPtr.Zero)); }
            catch { Stop(); throw; }
        }
        private static void Check(uint code) { if (code != 0) throw new InvalidOperationException("Windows audio playback failed (code " + code + ")."); }
        public void Stop()
        {
            if (!opened) return;
            mciSendString("stop " + alias, null, 0, IntPtr.Zero); mciSendString("close " + alias, null, 0, IntPtr.Zero); opened = false;
        }
        public void Dispose() { Stop(); }
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint mciSendString(string command, StringBuilder result, uint length, IntPtr callback);
    }
}
