using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using PSConverter;

internal static class ValidateNativeManifest
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Context
    {
        public uint Size,Flags;
        public string Source;
        public ushort Architecture,Language;
        public string AssemblyDirectory;
        public IntPtr ResourceName;
        public string ApplicationName;
        public IntPtr Module;
    }
    private static int Main(string[] args)
    {
        int failed=0;
        foreach (string name in new[] {"PS3_at3tool","PSVita_at9tool"})
        {
            string folder=PathSafety.LocalPath(Path.Combine(args[0],"codecs",name));
            var context=new Context {Size=(uint)Marshal.SizeOf(typeof(Context)),Flags=8,Architecture=0,
                Source=Path.Combine(folder,name+".exe"),AssemblyDirectory=folder,ResourceName=new IntPtr(1)};
            IntPtr handle=CreateActCtx(ref context);
            if (handle==new IntPtr(-1))
            {
                int error=Marshal.GetLastWin32Error();
                Console.WriteLine("FAIL activation manifest: "+name+"; "+error+" "+new Win32Exception(error).Message);
                failed++;continue;
            }
            ReleaseActCtx(handle);Console.WriteLine("PASS activation manifest: "+name+" (metadata only; encoder not executed)");
        }
        return failed==0?0:1;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateActCtx(ref Context context);
    [DllImport("kernel32.dll")] private static extern void ReleaseActCtx(IntPtr context);
}
