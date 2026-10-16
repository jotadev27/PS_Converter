using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using PSConverter;

internal static class ReadRuntimeMsi
{
    private static void Check(uint result) {if (result!=0) throw new IOException("MSI read failed: "+result);}
    private static string Text(uint record,uint field)
    {
        uint count=32768;var text=new StringBuilder((int)count);Check(MsiRecordGetString(record,field,text,ref count));return text.ToString();
    }
    private static void Query(uint database,string sql,Action<uint> row)
    {
        uint view;Check(MsiDatabaseOpenView(database,sql,out view));
        try
        {
            Check(MsiViewExecute(view,0));uint record;
            while (true)
            {
                uint result=MsiViewFetch(view,out record);if (result==259) break;Check(result);
                try {row(record);} finally {MsiCloseHandle(record);}
            }
        }
        finally {MsiCloseHandle(view);}
    }
    private static int Main(string[] args)
    {
        string source=PathSafety.LocalPath(args[0]),output=PathSafety.LocalPath(args[1]);Directory.CreateDirectory(output);
        uint database;Check(MsiOpenDatabase(source,IntPtr.Zero,out database));
        try
        {
            var mapping=new StringBuilder();
            Query(database,"SELECT `File`, `FileName` FROM `File`",record=>mapping.AppendLine(Text(record,1)+"\t"+Text(record,2)));
            File.WriteAllText(Path.Combine(output,"files.tsv"),mapping.ToString());
            try
            {
                Query(database,"SELECT `Text` FROM `Control` WHERE `Type` = 'ScrollableText'",record=>{
                    string license=Text(record,1);
                    if (license.Length>500) File.WriteAllText(Path.Combine(output,"runtime-license.rtf"),license);
                });
            }
            catch (IOException) {Console.WriteLine("No embedded scrollable license control in this MSI.");}
            Query(database,"SELECT `Name`, `Data` FROM `_Streams`",record=>{
                string name=Text(record,1);Console.WriteLine("Stream: "+name);
                if (!name.EndsWith(".cab",StringComparison.OrdinalIgnoreCase) && name.IndexOf("cab",StringComparison.OrdinalIgnoreCase)<0 && name.IndexOf("VC80_CRT_x86.msm",StringComparison.OrdinalIgnoreCase)<0) return;
                string path=Path.Combine(output,Path.GetFileName(name));
                using (var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    var buffer=new byte[65536];uint count;
                    do {count=(uint)buffer.Length;Check(MsiRecordReadStream(record,2,buffer,ref count));stream.Write(buffer,0,(int)count);} while (count!=0);
                }
                Console.WriteLine(Path.GetFileName(path));
            });
        }
        finally {MsiCloseHandle(database);}
        return 0;
    }
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiOpenDatabase(string path,IntPtr mode,out uint database);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiDatabaseOpenView(uint database,string query,out uint view);
    [DllImport("msi.dll")] private static extern uint MsiViewExecute(uint view,uint record);
    [DllImport("msi.dll")] private static extern uint MsiViewFetch(uint view,out uint record);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiRecordGetString(uint record,uint field,StringBuilder value,ref uint length);
    [DllImport("msi.dll")] private static extern uint MsiRecordReadStream(uint record,uint field,byte[] buffer,ref uint length);
    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(uint handle);
}
