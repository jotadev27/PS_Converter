using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PSConverter
{
    internal sealed class OptionsForm : Form
    {
        private readonly AppConfiguration configuration;
        private readonly ToolRegistry registry;
        private readonly ListView tools = new ListView();
        private readonly ComboBox bitrate = Theme.Combo();
        private readonly ComboBox loop = Theme.Combo();
        private readonly PropertyGrid advanced = new PropertyGrid();
        public CodecOptions Options { get; private set; }
        public int Bitrate { get; private set; }

        public OptionsForm(AppConfiguration configuration, bool toolTab, ConsoleProfile profile, int currentBitrate, bool decode)
        {
            this.configuration = configuration; registry = new ToolRegistry(configuration);
            Options = configuration.Options.Copy(); Bitrate = currentBitrate;
            Text = "Options - " + profile.Name; ClientSize = new Size(610,490); MinimumSize = new Size(610,490);
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Background; ForeColor = Theme.Text;
            Icon=Theme.ApplicationIcon(); ShowIcon=true;
            Font = Theme.Font(10,FontStyle.Regular); ShowInTaskbar = false;
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16,8) };
            var general = new TabPage("General") { BackColor = Theme.Surface, Padding = new Padding(22) };
            var advancedPage = new TabPage("Advanced") { BackColor = Theme.Surface };
            var encoders = new TabPage("Encoders") { BackColor = Theme.Surface, Padding = new Padding(14) };
            var rows = new TableLayoutPanel { Dock = DockStyle.Top, Height = 165, ColumnCount = 2, RowCount = 3 };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); rows.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            bitrate.DropDownStyle = ComboBoxStyle.DropDown;
            bitrate.Name="Bitrate"; bitrate.AccessibleName="Bitrate in kilobits per second";
            loop.AccessibleName="Loop mode";
            foreach (int value in profile.Bitrates) bitrate.Items.Add(value.ToString());
            bitrate.Text = currentBitrate.ToString(); bitrate.Enabled = !decode;
            loop.Items.AddRange(new object[] {"Whole track","No loop","Custom loop","Default whole track"});
            loop.SelectedIndex = (int)Options.Loop; loop.Enabled = !decode;
            loop.SelectedIndexChanged += delegate { Options.Loop = (LoopKind)loop.SelectedIndex; advanced.Refresh(); };
            rows.Controls.Add(Theme.Label("Bitrate / kbps",10,false),0,0); rows.Controls.Add(bitrate,1,0);
            rows.Controls.Add(Theme.Label("Loop",10,false),0,1); rows.Controls.Add(loop,1,1);
            var hint = Theme.Label(decode ? "Decode settings are in Advanced." : "Default: " + profile.DefaultBitrate + " kbps. Custom loops are set in Advanced.",9,false);
            hint.ForeColor = Theme.Muted; hint.Dock = DockStyle.Fill; hint.AutoSize = false;
            rows.Controls.Add(hint,0,2); rows.SetColumnSpan(hint,2); general.Controls.Add(rows);
            advanced.Dock = DockStyle.Fill; advanced.SelectedObject = Options; advanced.ToolbarVisible = false;
            advanced.PropertySort = PropertySort.Categorized; advanced.ViewBackColor = Theme.Surface; advanced.ViewForeColor = Theme.Text;
            advanced.HelpBackColor = Theme.Background; advanced.HelpForeColor = Theme.Muted; advanced.LineColor = Theme.Border;
            advanced.CategoryForeColor = Theme.Accent;
            advanced.PropertyValueChanged += delegate { loop.SelectedIndex = (int)Options.Loop; };
            advancedPage.Controls.Add(advanced);
            var note = Theme.Label("Encoders are included. Replace one only for a custom setup.",10,false);
            note.Dock = DockStyle.Top; note.AutoSize = false; note.Height = 37; note.ForeColor = Theme.Muted;
            tools.Dock = DockStyle.Fill; tools.View = View.Details; tools.FullRowSelect = true; tools.MultiSelect = false;
            tools.BackColor = Theme.Surface; tools.ForeColor = Theme.Text; tools.BorderStyle = BorderStyle.None;
            tools.Columns.Add("Console",180); tools.Columns.Add("Encoder",200); tools.Columns.Add("Status",130);
            tools.DoubleClick += delegate { RegisterSelected(); };
            var choose = Theme.Button("Use custom encoder...",false); choose.Dock = DockStyle.Bottom; choose.Height = 39;
            choose.Click += delegate { RegisterSelected(); };
            var trust = Theme.Label("Use encoders from a source you trust.",9,false);
            trust.Dock = DockStyle.Bottom; trust.Height = 28; trust.AutoSize = false; trust.ForeColor = Theme.Muted;
            encoders.Controls.Add(tools); encoders.Controls.Add(note); encoders.Controls.Add(trust); encoders.Controls.Add(choose);
            tabs.TabPages.Add(general); tabs.TabPages.Add(advancedPage); tabs.TabPages.Add(encoders);
            tabs.SelectedIndex = toolTab ? 2 : 0;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 57, Padding = new Padding(9), FlowDirection = FlowDirection.RightToLeft };
            var save = Theme.Button("Save",true); save.Width = 95;
            save.Click += delegate {
                int value;
                if (!int.TryParse(bitrate.Text,out value) || value <= 0)
                { MessageBox.Show(this,"Enter a positive bitrate.","Bitrate"); return; }
                Bitrate = value; DialogResult = DialogResult.OK; Close();
            };
            var cancel = Theme.Button("Cancel",false); cancel.Width = 95; cancel.DialogResult = DialogResult.Cancel;
            var reset = Theme.Button("Reset",false); reset.Width = 90;
            reset.Click += delegate { Options = new CodecOptions(); advanced.SelectedObject = Options; loop.SelectedIndex = 0; bitrate.Text = profile.DefaultBitrate.ToString(); };
            footer.Controls.Add(save); footer.Controls.Add(cancel); footer.Controls.Add(reset);
            Controls.Add(tabs); Controls.Add(footer); AcceptButton = save; CancelButton = cancel;
            RefreshTools(profile.Executable);
        }

        private void RefreshTools(string selectName)
        {
            tools.Items.Clear();
            foreach (ConsoleProfile profile in ConsoleProfile.All) AddToolRow(profile.Name,profile.Executable,selectName);
            AddToolRow("MP3 output","lame.exe",selectName);
        }
        private void AddToolRow(string display,string name,string selectName)
        {
            var item = new ListViewItem(new[] {display,name,registry.Has(name) ? "Ready" : "Missing files"}) { Tag = name };
            tools.Items.Add(item); if (name == selectName) item.Selected = true;
        }
        private void RegisterSelected()
        {
            if (tools.SelectedItems.Count == 0) return;
            string name = (string)tools.SelectedItems[0].Tag;
            try
            {
                ApprovedTool candidate = EncoderSetup.Choose(this,name);
                if (candidate == null) return;
                registry.Approve(candidate); RefreshTools(name);
            }
            catch (Exception ex) { MessageBox.Show(this,ex.Message,"Encoder",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        }
    }
}
