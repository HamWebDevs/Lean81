using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lean81
{
    public sealed partial class MainForm
    {
        readonly WorkspaceStore preferences;
        readonly UserWorkspace workspace;
        DeviceSnapshot device;
        CaptureSummary latestSummary;
        bool closeWhenIdle;
        bool choicesStale;
        string preferencesNotice;
        string CaptureMachineKey { get { return Product.Hash(demo ? "synthetic-demo-machine" : Environment.MachineName); } }
        bool SaveWorkspace()
        {
            Profiles.Copy(profile, workspace.Profile);
            if (preferences == null) return true;
            try { preferences.Save(workspace); preferencesNotice = null; return true; }
            catch (Exception ex) { preferencesNotice = "Preferences could not be saved; choices and new results stay in this session."; status.Text = preferencesNotice + " " + Product.Clean(ex.Message, 120); return false; }
        }
        void Overview()
        {
            Heading("Make room for what you use.", "Quick tune builds a reversible review. Resource capture shows CPU, RAM and disk pressure while you work.");
            var body = new Panel(); Body(body);
            var cards = new TableLayoutPanel { ColumnCount = 3, RowCount = 1 };
            for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            string[] labels = { "STARTUP TO REVIEW", "UNUSED SERVICES", "SAVED CAPTURES" };
            int[] counts = { report == null ? 0 : report.Items.Count(x => x.Kind == "startup" && x.CanChange), report == null ? 0 : report.Items.Count(x => x.Kind == "service" && x.CanChange), workspace.History.Count };
            for (int i = 0; i < 3; i++)
            {
                var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, i < 2 ? 12 : 0, 0), Padding = new Padding(14, 10, 8, 8) };
                var caption = LabelAt(labels[i], 8.5f); caption.Height = 24;
                var number = LabelAt(report == null && i < 2 ? "—" : counts[i].ToString(CultureInfo.InvariantCulture), 28, FontStyle.Bold); number.Height = 53;
                panel.Controls.Add(number); panel.Controls.Add(caption); cards.Controls.Add(panel, i, 0);
            }
            var guide = new Panel { BackColor = Color.White, Padding = new Padding(16, 10, 12, 8) };
            var text = LabelAt("1   Quick tune: choose features you use and build a review\n2   Trim recognized startup apps and optional desktop effects\n3   Capture CPU, memory and disk during a slowdown", 11); text.Dock = DockStyle.Fill; guide.Controls.Add(text);
            var actions = new FlowLayoutPanel { WrapContents = true };
            actions.Controls.Add(ActionButton("Quick tune", (s, e) => ShowPage(11), true));
            actions.Controls.Add(ActionButton("Recommendations", (s, e) => ShowPage(9)));
            actions.Controls.Add(ActionButton("Capture resources", (s, e) => ShowPage(6)));
            var second = new FlowLayoutPanel { WrapContents = true };
            second.Controls.Add(ActionButton("Rescan laptop", async (s, e) => await Scan()));
            second.Controls.Add(ActionButton("Export report…", (s, e) => ExportReport()));
            second.Controls.Add(ActionButton("Scan notes", (s, e) => ShowScanNotes()));
            var next = LabelAt(demo ? "SYNTHETIC DEMO · try the full workflow without system changes." : backend.IsAdministrator ? "Changes enabled · every selected change still needs review. Recovery holds the original settings." : "Inspection mode · scanning and resource captures need no admin rights. Enable changes from a review when ready.", 10);
            if (preferencesNotice != null) next.Text += "\n" + preferencesNotice;
            next.Padding = new Padding(2, 10, 0, 0);
            Rows(body, new Control[] { cards, guide, next, actions, second }, 102, 105, -1, 48, 48);
        }
        static string[] FeatureLabels = { "Printing, including PDF / virtual printers", "Bluetooth devices", "Indexed file and email search", "Touch, pen or the on-screen keyboard", "HomeGroup sharing", "Windows Media Player network sharing", "Offline network files", "Application preloading / Superfetch", "Scanners or camera image-acquisition software", "Portable-device library synchronization", "Windows error reporting and diagnostic assistance", "Shortcuts that track moved files" };
        static bool[] FeatureValues(UsageProfile p) { return new[] { p.Printing, p.Bluetooth, p.IndexedSearch, p.TouchKeyboard, p.HomeGroup, p.MediaSharing, p.OfflineFiles, p.Prefetch, p.Scanning, p.PortableDevices, p.ErrorReporting, p.LinkTracking }; }
        static UsageProfile ReadChecks(CheckBox[] checks)
        { return new UsageProfile { Printing = checks[0].Checked, Bluetooth = checks[1].Checked, IndexedSearch = checks[2].Checked, TouchKeyboard = checks[3].Checked, HomeGroup = checks[4].Checked, MediaSharing = checks[5].Checked, OfflineFiles = checks[6].Checked, Prefetch = checks[7].Checked, Scanning = checks[8].Checked, PortableDevices = checks[9].Checked, ErrorReporting = checks[10].Checked, LinkTracking = checks[11].Checked }; }
        CheckBox[] FeatureChecks(FlowLayoutPanel host, UsageProfile initial)
        {
            var values = FeatureValues(initial); var checks = new CheckBox[FeatureLabels.Length];
            for (int i = 0; i < checks.Length; i++) { checks[i] = new CheckBox { Text = FeatureLabels[i], Checked = values[i], AutoSize = true, Margin = new Padding(0, 3, 0, 5), MinimumSize = new Size(540, 25) }; host.Controls.Add(checks[i]); }
            return checks;
        }
        ComboBox Presets(CheckBox[] checks)
        {
            var presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 325, Margin = new Padding(0, 4, 12, 0) };
            presets.Items.Add("Use my current answers"); presets.Items.AddRange(Profiles.Names); presets.SelectedIndex = 0;
            presets.SelectedIndexChanged += (s, e) => { if (presets.SelectedIndex > 0) { var values = FeatureValues(Profiles.Preset(presets.SelectedIndex - 1)); for (int i = 0; i < checks.Length; i++) checks[i].Checked = values[i]; } };
            return presets;
        }
        async Task QuickSetup(string screenshot = null)
        {
            if (busy) return;
            UsageProfile answers;
            using (var dialog = new Form { Text = "Lean81 · quick setup", ClientSize = new Size(680, 525), StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = Canvas, Padding = new Padding(20), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var note = LabelAt("Keep each feature checked if you use it or aren't sure. Presets only fill these answers.\nNothing is disabled here. Next, review the specific services and their tradeoffs.", 10.5f);
                var list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White, Padding = new Padding(14, 8, 8, 8) };
                var checks = FeatureChecks(list, profile); var presets = Presets(checks);
                var options = new FlowLayoutPanel(); options.Controls.Add(presets);
                var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft };
                var cancel = ActionButton("Cancel", (s, e) => dialog.DialogResult = DialogResult.Cancel);
                actions.Controls.Add(ActionButton("Save answers & continue", (s, e) => dialog.DialogResult = DialogResult.OK, true)); actions.Controls.Add(cancel);
                var root = new Panel { Dock = DockStyle.Fill }; Rows(root, new Control[] { note, options, list, actions }, 65, 42, -1, 45); dialog.Controls.Add(root); dialog.CancelButton = cancel;
                if (screenshot != null) AutoScreenshotAndCancel(dialog, screenshot);
                if (dialog.ShowDialog(this) != DialogResult.OK) return; answers = ReadChecks(checks);
            }
            Profiles.Copy(answers, profile); selected.Clear(); bool saved = SaveWorkspace(); page = 4; await Scan();
            status.Text = (saved ? "Answers saved" : "Answers ready in this session") + " · select the unused services you want to review";
        }
        void Profile()
        {
            Heading("What do you use?", "Keep uncertain features checked. Presets fill your answers; changes still need an individual review.");
            var body = new Panel(); Body(body);
            var list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White, Padding = new Padding(16, 8, 8, 8) };
            var checks = FeatureChecks(list, profile);
            foreach (var check in checks) check.CheckedChanged += (s, e) => { Profiles.Copy(ReadChecks(checks), profile); choicesStale = true; selected.Clear(); status.Text = "Answers changed · Save & refresh choices before selecting settings"; UpdateSelection(); };
            var choices = new FlowLayoutPanel(); choices.Controls.Add(Presets(checks));
            var actions = new FlowLayoutPanel();
            actions.Controls.Add(ActionButton("Save & refresh choices", async (s, e) => { SaveWorkspace(); page = 4; await Scan(); }, true));
            actions.Controls.Add(ActionButton("Quick setup…", async (s, e) => await QuickSetup()));
            Rows(body, new Control[] { choices, list, actions }, 43, -1, 48);
        }
        void RecommendationsPage()
        {
            Heading("Choose the next useful step.", "Recommendations explain observations and your answers. They never apply changes automatically.");
            var body = new Panel(); Body(body); var grid = Grid(); grid.ReadOnly = true;
            grid.Columns.Add("Title", "Next step"); grid.Columns.Add("Reason", "Why it may help"); grid.Columns[0].FillWeight = 38; grid.Columns[1].FillWeight = 62;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            foreach (var rec in Recommendations.Build(report, device, latestSummary ?? workspace.History.FirstOrDefault(), profile)) { var row = grid.Rows[grid.Rows.Add(rec.Title, rec.Reason)]; row.Tag = rec; }
            var actions = new FlowLayoutPanel();
            var open = ActionButton("Open selected step", (s, e) => { if (grid.SelectedRows.Count > 0) { var rec = grid.SelectedRows[0].Tag as Recommendation; if (rec != null) ShowPage(rec.Page); } }, true); actions.Controls.Add(open);
            actions.Controls.Add(ActionButton("Quick setup…", async (s, e) => await QuickSetup()));
            Rows(body, new Control[] { grid, actions }, -1, 48);
        }
        void Apps()
        {
            Heading("Review unused desktop apps.", "Filter by name or publisher, then use the normal Windows uninstaller for software you recognize.");
            var body = new Panel(); Body(body); var toolbar = new FlowLayoutPanel();
            var search = new TextBox { Width = 250, Margin = new Padding(0, 5, 12, 0) }; toolbar.Controls.Add(search);
            toolbar.Controls.Add(ActionButton("Programs and Features", (s, e) => OpenTool("apps"), true));
            var grid = Grid(); grid.ReadOnly = true; grid.Columns.Add("Name", "Application"); grid.Columns.Add("Publisher", "Publisher"); grid.Columns.Add("Version", "Version"); grid.Columns.Add("Note", "Review note");
            grid.Columns[0].FillWeight = 30; grid.Columns[1].FillWeight = 22; grid.Columns[2].FillWeight = 12; grid.Columns[3].FillWeight = 36;
            Action populate = () => { grid.Rows.Clear(); if (report != null) foreach (var app in report.Apps.Where(a => (a.Name + " " + a.Publisher).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(a => a.Name)) grid.Rows.Add(app.Name, app.Publisher, app.Version, app.Note); };
            search.TextChanged += (s, e) => populate(); populate(); Rows(body, new Control[] { toolbar, grid }, 47, -1);
        }
        void DevicePage()
        {
            Heading("Know the limits of this laptop.", "An on-demand hardware, memory, power and fixed-drive capacity snapshot. No files are removed.");
            var body = new Panel(); Body(body);
            var facts = LabelAt(device == null ? "Scan this laptop to load device information." : device.Cpu + "\n" + device.Windows + " · " + device.Architecture + " · " + device.LogicalCpus + " logical processors\nPhysical memory: " + DeviceSnapshot.Size(device.TotalMemory) + " · available: " + DeviceSnapshot.Size(device.AvailableMemory) + "\nPower: " + device.Power + (device.BatteryPercent.HasValue ? " · battery " + device.BatteryPercent + "%" : "") + "\nUptime since Windows boot: " + TimeSpan.FromMilliseconds(device.UptimeMilliseconds).ToString(@"d\d\ hh\h\ mm\m", CultureInfo.InvariantCulture), 10.5f);
            facts.BackColor = Color.White; facts.Padding = new Padding(12, 9, 4, 0);
            if (device != null && device.Notes.Count > 0) facts.Text += "\nPartial snapshot: " + device.Notes.Count + " readings were unavailable.";
            var grid = Grid(); grid.ReadOnly = true; grid.Columns.Add("Drive", "Drive"); grid.Columns.Add("Size", "Total GiB"); grid.Columns.Add("Free", "Available GiB"); grid.Columns.Add("Percent", "Available %"); grid.Columns.Add("Status", "Observation");
            grid.Columns[0].FillWeight = 12; grid.Columns[1].FillWeight = 17; grid.Columns[2].FillWeight = 21; grid.Columns[3].FillWeight = 19; grid.Columns[4].FillWeight = 31;
            if (device != null) foreach (var disk in device.Disks) grid.Rows.Add(disk.Drive, (disk.TotalBytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture), (disk.FreeBytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture), disk.FreePercent.ToString("0.0", CultureInfo.InvariantCulture), disk.LowSpace ? "Low free space" : "Capacity observed");
            var note = LabelAt("Available space is reported for your account. This is not a disk-health, SSD or temperature test.\nDisk Cleanup opens Windows' own choices; it does not start a Lean81 cleanup.", 9);
            var actions = new FlowLayoutPanel();
            actions.Controls.Add(ActionButton("Refresh snapshot", async (s, e) => { if (busy) return; SetBusy(true, "Reading device and storage capacity…"); try { device = await Task.Run(() => demo ? DeviceSnapshot.Demo() : DeviceSnapshot.Read()); ShowPage(8); } catch (Exception ex) { Error(ex); } finally { SetBusy(false, "Device snapshot refreshed"); } }));
            actions.Controls.Add(ActionButton("Windows Disk Cleanup", (s, e) => OpenTool("cleanup")));
            actions.Controls.Add(ActionButton("Export report…", (s, e) => ExportReport()));
            Rows(body, new Control[] { facts, grid, note, actions }, 145, -1, 50, 48);
        }
        void CapturePage()
        {
            Heading("Find what is holding you up.", "Two-second CPU, RAM and disk samples. Capture during a slowdown, then inspect the resource details.");
            var tabs = new TabControl(); Body(tabs); var live = new TabPage("Capture") { Padding = new Padding(8) }; var saved = new TabPage("Saved results & baseline") { Padding = new Padding(8) }; var resources = new TabPage("Memory, disk & processes") { Padding = new Padding(8) }; tabs.TabPages.Add(live); tabs.TabPages.Add(saved); tabs.TabPages.Add(resources);
            var body = new Panel { Dock = DockStyle.Fill }; live.Controls.Add(body);
            var toolbar = new FlowLayoutPanel(); toolbar.Controls.Add(new Label { Text = "Workload", AutoSize = true, Padding = new Padding(0, 7, 8, 0) });
            var workload = new TextBox { Text = workspace.Workload, Width = 215, MaxLength = 80, Margin = new Padding(0, 3, 12, 0) }; toolbar.Controls.Add(workload); workload.TextChanged += (s, e) => workspace.Workload = Product.Clean(workload.Text, 80);
            var duration = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(0, 3, 8, 0) }; duration.Items.AddRange(new object[] { "30 seconds", "60 seconds", "120 seconds" }); duration.SelectedIndex = 0; toolbar.Controls.Add(duration);
            captureStats = LabelAt("Ready. Use the laptop normally during capture.", 10.5f);
            chart = new CpuChart();
            var actions = new FlowLayoutPanel { WrapContents = true };
            var start = ActionButton("Start capture", async (s, e) => await StartCapture(duration.SelectedIndex == 0 ? 30 : duration.SelectedIndex == 1 ? 60 : 120), true); actions.Controls.Add(start);
            var stop = ActionButton("Stop", (s, e) => { if (captureCancel != null) captureCancel.Cancel(); }); stop.Enabled = false; actions.Controls.Add(stop);
            var export = ActionButton("Export trace…", (s, e) => ExportCapture()); export.Enabled = capture != null; actions.Controls.Add(export);
            var peaks = LabelAt("Teal: CPU   ·   Blue: physical RAM in use   ·   Amber: aggregate disk busy\n" + (capture == null ? "Up to 20 CPU, memory and I/O leaders per sample. Monitoring stops with capture." : "Open Memory, disk & processes for sortable leaders and findings."), 9.5f);
            Rows(body, new Control[] { toolbar, captureStats, chart, peaks, actions }, 40, 42, -1, 52, 46);
            if (capture != null) { chart.Values.AddRange(capture.Samples.Select(s => ResourceAnalysis.Number(s.metrics, "cpu_pct", 100))); chart.MemoryValues.AddRange(capture.Samples.Select(s => { var available = ResourceAnalysis.Number(s.metrics, "memory_available_pct", 100); return available.HasValue ? (double?)(100 - available.Value) : null; })); chart.DiskValues.AddRange(capture.Samples.Select(s => ResourceAnalysis.Number(s.metrics, "disk_busy_pct", 100))); ShowCaptureStats(); }
            captureControls = running => { start.Enabled = !running; stop.Enabled = running; workload.Enabled = !running; duration.Enabled = !running; export.Enabled = !running && capture != null; tabs.TabPages[1].Enabled = tabs.TabPages[2].Enabled = !running; };
            SavedResults(saved);
            ResourceDetails(resources);
        }
        void SavedResults(TabPage pageTab)
        {
            var body = new Panel { Dock = DockStyle.Fill }; pageTab.Controls.Add(body);
            var grid = Grid(); grid.ReadOnly = true; grid.Columns.Add("Date", "UTC / workload"); grid.Columns.Add("Average", "Avg CPU"); grid.Columns.Add("Peak", "Peak CPU"); grid.Columns.Add("Seconds", "Observed s"); grid.Columns.Add("Power", "Power / state");
            grid.Columns[0].FillWeight = 35; grid.Columns[1].FillWeight = 14; grid.Columns[2].FillWeight = 14; grid.Columns[3].FillWeight = 15; grid.Columns[4].FillWeight = 22;
            grid.Columns[0].DefaultCellStyle.WrapMode = DataGridViewTriState.True; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            foreach (var result in workspace.History)
            {
                var row = grid.Rows[grid.Rows.Add(result.CreatedUtc.Substring(0, 19).Replace("T", " ") + "\n" + result.Workload, result.AverageCpu.ToString("0.0", CultureInfo.InvariantCulture) + "%", result.PeakCpu.ToString("0.0", CultureInfo.InvariantCulture) + "%", result.ObservedSeconds.ToString("0", CultureInfo.InvariantCulture), ResultState(result))]; row.Tag = result;
                row.Cells[0].ToolTipText = result.Workload;
            }
            var detail = new TextBox { Multiline = true, ReadOnly = true, BackColor = Color.White, ScrollBars = ScrollBars.Vertical, Text = "Choose a saved result. Only aggregate numbers and your workload label are saved; process names, paths and PIDs are not stored in history." };
            grid.SelectionChanged += (s, e) => { if (grid.SelectedRows.Count == 0) return; var current = grid.SelectedRows[0].Tag as CaptureSummary; var baseline = workspace.History.FirstOrDefault(r => r.Id == workspace.BaselineId); if (current != null && baseline != null) detail.Text = CaptureComparison.Compare(baseline, current).Text(); };
            var actions = new FlowLayoutPanel { WrapContents = true };
            actions.Controls.Add(ActionButton("Use as baseline", (s, e) => { if (grid.SelectedRows.Count == 0) return; var result = grid.SelectedRows[0].Tag as CaptureSummary; if (result == null) return; workspace.BaselineId = result.Id; bool persisted = SaveWorkspace(); foreach (DataGridViewRow row in grid.Rows) row.Cells[4].Value = ResultState((CaptureSummary)row.Tag); detail.Text = (persisted ? "Baseline saved: " : "Baseline for this session: ") + result.Workload + "\r\n" + result.CreatedUtc + "\r\nNow repeat the same activity, duration and power state, then compare another saved result."; status.Text = persisted ? "Baseline saved locally" : preferencesNotice; }));
            actions.Controls.Add(ActionButton("Compare selected", (s, e) => { if (grid.SelectedRows.Count == 0) return; var baseline = workspace.History.FirstOrDefault(r => r.Id == workspace.BaselineId); if (baseline == null) { detail.Text = "Choose a result and click Use as baseline first."; return; } detail.Text = CaptureComparison.Compare(baseline, (CaptureSummary)grid.SelectedRows[0].Tag).Text(); }, true));
            actions.Controls.Add(ActionButton("Delete selected result", (s, e) => { if (grid.SelectedRows.Count == 0) return; var result = (CaptureSummary)grid.SelectedRows[0].Tag; workspace.History.Remove(result); if (workspace.BaselineId == result.Id) workspace.BaselineId = null; SaveWorkspace(); ShowPage(6); ((TabControl)pageLayout.GetControlFromPosition(0, 2)).SelectedIndex = 1; }));
            actions.Controls.Add(ActionButton("Export selected…", (s, e) => { if (grid.SelectedRows.Count > 0) ExportReport((CaptureSummary)grid.SelectedRows[0].Tag); }));
            Rows(body, new Control[] { grid, detail, actions }, -1, 138, 46);
            grid.ClearSelection(); if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
        }
        string ResultState(CaptureSummary result) { return result.Power + (result.Synthetic ? " · demo" : "") + (result.Id == workspace.BaselineId ? " · baseline" : !result.Complete ? " · partial" : ""); }
        void ExportReport(CaptureSummary selectedResult = null, string screenshot = null)
        {
            if (report == null) { status.Text = "Scan this laptop before exporting a report."; return; }
            var current = selectedResult ?? latestSummary ?? workspace.History.FirstOrDefault();
            var baseline = workspace.History.FirstOrDefault(r => r.Id == workspace.BaselineId);
            using (var options = new Form { Text = "Export laptop report", ClientSize = new Size(610, 340), StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = Canvas, Padding = new Padding(18), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var text = LabelAt("Save a self-contained HTML report with next steps, device capacity and CPU evidence. Open or print it in a browser. Commands, paths, receipts, workload labels and machine identifiers are excluded.", 10);
                var comparison = new CheckBox { Text = baseline == null ? "Baseline comparison (choose a baseline in Saved results first)" : "Include baseline comparison and condition checks", AutoSize = true, Checked = baseline != null && current != null, Enabled = baseline != null && current != null };
                var history = new CheckBox { Text = "Include saved aggregate history (up to 12 results)", AutoSize = true };
                var names = new CheckBox { Text = "Include software names in this report", AutoSize = true };
                var privacy = LabelAt("Review the report before sharing: hardware details and capture dates remain visible.\nNo upload is performed. Mismatched captures show reasons instead of differences.", 9);
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft }; var cancel = ActionButton("Cancel", (s, e) => options.DialogResult = DialogResult.Cancel);
                buttons.Controls.Add(ActionButton("Choose file…", (s, e) => options.DialogResult = DialogResult.OK, true)); buttons.Controls.Add(cancel);
                var panel = new Panel { Dock = DockStyle.Fill }; Rows(panel, new Control[] { text, comparison, history, names, privacy, buttons }, -1, 33, 33, 33, 52, 40); options.Controls.Add(panel); options.CancelButton = cancel;
                if (screenshot != null) AutoScreenshotAndCancel(options, screenshot);
                if (options.ShowDialog(this) != DialogResult.OK) return;
                using (var save = new SaveFileDialog { Filter = "Local HTML report (*.html)|*.html", FileName = "lean81-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".html" })
                    if (save.ShowDialog(this) == DialogResult.OK) try { File.WriteAllText(save.FileName, Reports.Build(report, device, current, comparison.Checked ? baseline : null, workspace.History, profile, new ReportOptions { IncludeSoftwareNames = names.Checked, IncludeHistory = history.Checked, Synthetic = demo }), new UTF8Encoding(false)); status.Text = "Report saved locally · open it in your browser to read or print"; } catch (Exception ex) { Error(ex); }
            }
        }
        void ToolsPage()
        {
            Heading("Help when you need it.", "Open a specific Windows tool or read how Lean81's choices, captures and recovery work.");
            var tabs = new TabControl(); Body(tabs); var tools = new TabPage("Windows tools") { Padding = new Padding(8) }; var help = new TabPage("How it works") { Padding = new Padding(8) }; tabs.TabPages.Add(tools); tabs.TabPages.Add(help);
            var body = new Panel { Dock = DockStyle.Fill }; tools.Controls.Add(body); var grid = Grid(); grid.ReadOnly = true; grid.Columns.Add("Name", "Tool"); grid.Columns.Add("Purpose", "Purpose"); grid.Columns[0].FillWeight = 29; grid.Columns[1].FillWeight = 71; grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            foreach (var tool in WindowsTools.All) { var row = grid.Rows[grid.Rows.Add(tool.Title, tool.Purpose)]; row.Tag = tool; }
            var actions = new FlowLayoutPanel(); actions.Controls.Add(ActionButton("Open selected tool", (s, e) => { if (grid.SelectedRows.Count > 0) OpenTool(((ToolSpec)grid.SelectedRows[0].Tag).Id); }, true)); actions.Controls.Add(ActionButton("Startup folder", (s, e) => OpenStartup()));
            Rows(body, new Control[] { grid, actions }, -1, 48);
            help.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = Color.White, ScrollBars = ScrollBars.Vertical, Text = HelpText });
        }
        void OpenTool(string id)
        {
            var tool = WindowsTools.All.FirstOrDefault(t => t.Id == id); if (tool == null) return;
            string args = tool.Args;
            if (args.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase)) args = "\"" + Path.Combine(Environment.SystemDirectory, args) + "\"";
            OpenSystem(tool.File, args);
        }
        static void AutoScreenshotAndCancel(Form dialog, string path)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 150 };
            timer.Tick += (s, e) => { timer.Stop(); using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); } dialog.DialogResult = DialogResult.Cancel; };
            dialog.Shown += (s, e) => timer.Start(); dialog.FormClosed += (s, e) => timer.Dispose();
        }
        internal void SmokeSetupScreenshot(string path)
        {
            if (!demo) throw new InvalidOperationException("Synthetic mode required.");
            string before = Product.Json().Serialize(profile); QuickSetup(path).GetAwaiter().GetResult();
            if (Product.Json().Serialize(profile) != before) throw new InvalidOperationException("Cancelled setup changed usage answers.");
        }
        internal void SmokeComparisonScreenshot(string path)
        {
            SmokePage(6); var tabs = pageLayout.GetControlFromPosition(0, 2) as TabControl; tabs.SelectedIndex = 1; Refresh(); ValidateSmokeLayout(); SaveScreenshot(path);
        }
        internal void SmokeReport(string directory)
        {
            if (!demo) throw new InvalidOperationException("Synthetic mode required.");
            string before = Product.Json().Serialize(workspace);
            ExportReport(null, Path.Combine(directory, "report-options.png"));
            if (before != Product.Json().Serialize(workspace)) throw new InvalidOperationException("Cancelled report options changed the workspace.");
            var baseline = CaptureSummary.From(CaptureResult.Demo(), "Report fixture", CaptureMachineKey);
            var current = CaptureSummary.From(CaptureResult.Demo(), "Report fixture", CaptureMachineKey); current.AverageCpu -= 5;
            var options = new ReportOptions { IncludeHistory = true, Synthetic = true };
            File.WriteAllText(Path.Combine(directory, "report-demo.html"), Reports.Build(report, device, current, baseline, new[] { current, baseline }, profile, options), new UTF8Encoding(false));
            current.Power = "battery";
            File.WriteAllText(Path.Combine(directory, "report-mismatched.html"), Reports.Build(report, device, current, baseline, new[] { current, baseline }, profile, options), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "report-no-capture.html"), Reports.Build(report, null, null, false, true), new UTF8Encoding(false));
        }
        static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
        internal void SmokeInteractions()
        {
            SmokePage(4); var grid = Descendants(content).OfType<DataGridView>().Single();
            var eligible = grid.Rows.Cast<DataGridViewRow>().First(r => ((ScanItem)r.Tag).CanChange);
            eligible.Cells[0].Value = true;
            if (selected.Count != 1 || !review.Enabled) throw new InvalidOperationException("Selection is not connected to review.");
            eligible.Cells[0].Value = false;
            if (selected.Count != 0 || review.Enabled) throw new InvalidOperationException("Clearing selection did not clear review.");
            if (grid.Rows.Cast<DataGridViewRow>().Where(r => !((ScanItem)r.Tag).CanChange).Any(r => !r.Cells[0].ReadOnly)) throw new InvalidOperationException("A kept row is selectable.");
            var filter = Descendants(content).OfType<TextBox>().Single(t => !t.Multiline); filter.Text = "zz-no-such-service";
            if (grid.Rows.Count != 0) throw new InvalidOperationException("Inventory filter did not filter."); filter.Text = "";
            if (grid.Rows.Count == 0) throw new InvalidOperationException("Clearing filter did not restore inventory.");
            var before = Product.Json().Serialize(profile); ShowPage(1);
            var answer = Descendants(content).OfType<CheckBox>().First(); answer.Checked = !answer.Checked;
            if (!choicesStale) throw new InvalidOperationException("Changing a usage answer did not invalidate old choices.");
            ShowPage(4); grid = Descendants(content).OfType<DataGridView>().Single();
            if (grid.Rows.Cast<DataGridViewRow>().Any(r => !r.Cells[0].ReadOnly) || review.Enabled) throw new InvalidOperationException("A stale scan still allows selection.");
            Profiles.Copy(Product.Json().Deserialize<UsageProfile>(before), profile); choicesStale = false; report = backend.Scan(profile); ShowPage(4);
            foreach (var tool in WindowsTools.All) OpenTool(tool.Id);
        }
        const string HelpText = "START HERE\r\nUse Quick tune, Quick setup or Usage profile. Quick tune builds a review from unused-feature answers; desktop effects require a separate opt-in. Keep features checked if you use them or aren't sure. Presets only fill answers; they never apply changes. Select specific entries and read the review screen.\r\n\r\nWHY CAN'T I SELECT THIS?\r\nA feature can be kept by your answers, needed by a dependent service, already disabled, or protected by policy. The Why / next step column explains it. Core, networking and security services are outside the allowlist.\r\n\r\nADMINISTRATOR MODE\r\nScans, device snapshots, captures and reports work without admin. A change review can reopen Lean81 with administrator permission using the same Windows account. Usage answers are remembered; selections are intentionally made again against a fresh scan. No change plan is imported into the privileged process.\r\n\r\nCPU BASELINES\r\nGive your activity a short workload label. Run a 30-, 60- or 120-second capture, then set its saved result as the baseline. Repeat the same activity, duration and power source before comparing another result. Unknown/mixed power, different hardware, missing samples and partial captures block comparison. Matching labels do not prove matching activity.\r\n\r\nCAPTURE LIMITS\r\nSamples are two seconds apart. Up to 20 accessible CPU, memory and read/write I/O leaders are recorded per sample. CPU is normalized across logical processors. Process I/O includes network and device operations, not just disk; unavailable counters remain unknown. Memory, disk & processes shows commit usage, page-ins and kernel pools. Short spikes and inaccessible processes may be missed. There is no wake-delay probe. Capturing has an observer cost, recorded in the trace. No continuous background monitor runs.\r\n\r\nLOCAL DATA & PRIVACY\r\nUp to 12 aggregate CPU results, workload labels, baseline and usage answers are stored under LocalAppData\\Lean81. History has no process names, PIDs, startup commands or paths. Demo mode uses memory only. Exported traces omit process names by default. HTML reports omit software names unless selected. Review any export before sharing. No telemetry or upload is performed.\r\n\r\nRECOVERY\r\nKeep ProgramData\\Lean81: it holds administrator-private original settings. Recovery checks the entire pending receipt before restoring. Independent edits or partial states stop automatic recovery. Check the receipt, read its error and inspect the corresponding Windows tool. Do not edit receipts to force recovery. Private receipts can contain startup commands; do not share them.\r\n\r\nWHAT CHANGES DO\r\nA startup change removes one supported Run value. A task change prevents future automatic launches; it does not stop a running task. A service change disables its start type and stops that service without stopping dependents. Desktop effects write three fixed user DWORD settings only after selection; exact previous values, including absence, are saved. Sign out and back in to refresh Explorer. No process killing, automatic uninstalling or file cleanup is performed.\r\n\r\nWINDOWS 8.1\r\nLean81 targets .NET Framework 4.5.1 / Windows 8.1. Automated checks run on newer Windows; physical Windows 8.1 validation is pending. No performance improvement is promised without measurements on your laptop.";
    }
}
