using System;
using System.IO;
using PSConverter;

internal static class RuntimeArchive
{
    // Inspect signed Microsoft download bytes as data. This utility never launches an installer.
    private static int Main(string[] args)
    {
        string source=PathSafety.LocalPath(args[0]),destination=PathSafety.LocalPath(args[1]);
        Directory.CreateDirectory(destination);byte[] bytes=File.ReadAllBytes(source);int found=0;
        for (int offset=0;offset<=bytes.Length-36;offset++)
        {
            if (bytes[offset]!=77 || bytes[offset+1]!=83 || bytes[offset+2]!=67 || bytes[offset+3]!=70) continue;
            uint length=BitConverter.ToUInt32(bytes,offset+8);
            if (length<36 || (long)offset+length>bytes.Length || BitConverter.ToUInt32(bytes,offset+4)!=0) continue;
            string target=Path.Combine(destination,"archive-"+(found++)+".cab");
            using (var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) output.Write(bytes,offset,checked((int)length));
            Console.WriteLine(Path.GetFileName(target)+": "+length+" bytes");
        }
        if (found==0) {Console.Error.WriteLine("No complete cabinet was found.");return 1;}
        return 0;
    }
}
