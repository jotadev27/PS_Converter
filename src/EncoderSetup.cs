using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PSConverter
{
    internal static class EncoderSetup
    {
        public static ApprovedTool Choose(IWin32Window owner, string name)
        {
            using (var dialog = new OpenFileDialog {
                Title = "Set up conversion: select " + name,
                Filter = "Required encoder (" + name + ")|" + name,
                FileName = name, CheckFileExists = true, Multiselect = false,
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                ApprovedTool candidate = ToolRegistry.Inspect(name, dialog.FileName);
                string hash = candidate.Files.First(f => Path.GetFileName(f.Path).Equals(name, StringComparison.OrdinalIgnoreCase)).Sha256;
                if (MessageBox.Show(owner, "Use " + name + " for conversion?\n\nSHA-256: " + hash +
                    "\n\nOnly approve encoders from a source you trust.", "Approve encoder",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return null;
                return candidate;
            }
        }
    }
}
