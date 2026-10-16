using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Capture only the installer started by this check; never capture the desktop.
internal static class InstallerWizardChecks
{
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr state);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Bounds bounds);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rectangle, IntPtr region, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int kind, ref ProcessInfo info, int length, out int returned);
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Reserved1, Peb, Reserved2, Reserved3, Id, Parent; }

    private static string Text(IntPtr window, bool className)
    {
        var text=new StringBuilder(512);
        if (className) GetClassName(window,text,text.Capacity); else GetWindowText(window,text,text.Capacity);
        return text.ToString();
    }
    private static bool BelongsTo(int candidate, int owner)
    {
        for (int depth=0;depth<4 && candidate>0;depth++)
        {
            if (candidate==owner) return true;
            using (var process=Process.GetProcessById(candidate))
            {
                var info=new ProcessInfo();int returned;
                if (NtQueryInformationProcess(process.Handle,0,ref info,Marshal.SizeOf(typeof(ProcessInfo)),out returned)!=0) return false;
                candidate=info.Parent.ToInt32();
            }
        }
        return false;
    }
    private static IntPtr Button(IntPtr wizard, string text)
    {
        IntPtr found=IntPtr.Zero;
        EnumChildWindows(wizard,delegate(IntPtr child,IntPtr unused) {
            if (IsWindowVisible(child) && Text(child,false).Replace("&","")==text) found=child;
            return true;
        },IntPtr.Zero);
        if (found==IntPtr.Zero) throw new InvalidOperationException("Missing wizard control: "+text);
        return found;
    }
    private static void Click(IntPtr button)
    {
        if (!IsWindowEnabled(button)) throw new InvalidOperationException("A wizard button is disabled.");
        SendMessage(button,0x00F5,IntPtr.Zero,IntPtr.Zero);Thread.Sleep(250);
    }
    private static void Capture(IntPtr wizard,string path)
    {
        RedrawWindow(wizard,IntPtr.Zero,IntPtr.Zero,0x0185);Thread.Sleep(150);
        Bounds bounds;if (!GetWindowRect(wizard,out bounds)) throw new InvalidOperationException("Missing installer bounds.");
        using (var bitmap=new Bitmap(bounds.Right-bounds.Left,bounds.Bottom-bounds.Top))
        using (var graphics=Graphics.FromImage(bitmap))
        {
            IntPtr dc=graphics.GetHdc();bool captured;
            try {captured=PrintWindow(wizard,dc,2);} finally {graphics.ReleaseHdc(dc);}
            if (!captured) throw new InvalidOperationException("Installer-only capture failed.");
            bitmap.Save(path,ImageFormat.Png);
        }
    }
    private static int Main(string[] args)
    {
        if (args.Length!=2) throw new ArgumentException("Expected installer and preview directory.");
        string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        IntPtr wizard=IntPtr.Zero;int childId=0;
        using (var launcher=Process.Start(new ProcessStartInfo {
            FileName=Path.GetFullPath(args[0]),Arguments="/SP- /DIR=\""+Path.Combine(output,"install-check")+"\"",
            UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden }))
        {
            try
            {
                for (int attempt=0;attempt<100 && wizard==IntPtr.Zero;attempt++)
                {
                    EnumWindows(delegate(IntPtr window,IntPtr unused) {
                        if (Text(window,true)!="TWizardForm" || Text(window,false)!="Setup - PS_Converter") return true;
                        int process;GetWindowThreadProcessId(window,out process);
                        if (BelongsTo(process,launcher.Id)) {wizard=window;childId=process;}
                        return true;
                    },IntPtr.Zero);
                    Thread.Sleep(100);
                }
                if (wizard==IntPtr.Zero) throw new InvalidOperationException("The classic wizard did not appear.");
                SetWindowPos(wizard,IntPtr.Zero,-12000,-12000,0,0,0x0015);Thread.Sleep(250);
                Capture(wizard,Path.Combine(output,"welcome.png"));
                Click(Button(wizard,"Next"));
                if (IsWindowEnabled(Button(wizard,"Next"))) throw new InvalidOperationException("License acceptance is not enforced.");
                Capture(wizard,Path.Combine(output,"license.png"));
                Click(Button(wizard,"I accept the agreement"));
                Click(Button(wizard,"Next"));
                Capture(wizard,Path.Combine(output,"destination.png"));
                Click(Button(wizard,"Next"));
                Button(wizard,"Install");Capture(wizard,Path.Combine(output,"ready.png"));
                Console.WriteLine("PASS native Inno wizard: welcome, license acceptance gating, destination, install confirmation; installer-window-only previews.");
                return 0;
            }
            finally
            {
                if (childId!=0) using (var child=Process.GetProcessById(childId)) {if (!child.HasExited) child.Kill();}
                if (!launcher.HasExited) launcher.Kill();
            }
        }
    }
}
