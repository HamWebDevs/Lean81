using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lean81
{
    public sealed class CpuChart : Panel
    {
        public readonly List<double?> Values = new List<double?>();
        public readonly List<double?> MemoryValues = new List<double?>(), DiskValues = new List<double?>();
        public CpuChart() { DoubleBuffered = true; BackColor = Color.White; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var plot = new Rectangle(44, 18, Math.Max(1, Width - 68), Math.Max(1, Height - 48));
            using (var grid = new Pen(Color.FromArgb(228, 234, 238)))
            using (var text = new SolidBrush(Color.FromArgb(105, 119, 132)))
                for (int n = 0; n <= 4; n++)
                {
                    float y = plot.Top + plot.Height * n / 4f; g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    g.DrawString((100 - n * 25).ToString(CultureInfo.InvariantCulture), Font, text, 6, y - 7);
                }
            if (Values.Count < 2) return;
            if (Values.All(v => v.HasValue))
            {
                var points = Values.Select((v, i) => new PointF(plot.Left + i * plot.Width / (float)Math.Max(14, Values.Count - 1), plot.Bottom - (float)v.Value / 100 * plot.Height)).ToArray();
                var area = new List<PointF> { new PointF(points[0].X, plot.Bottom) }; area.AddRange(points); area.Add(new PointF(points[points.Length - 1].X, plot.Bottom));
                using (var fill = new SolidBrush(Color.FromArgb(30, 15, 145, 125))) g.FillPolygon(fill, area.ToArray());
            }
            Series(g, plot, Values, Color.FromArgb(13, 137, 119));
            Series(g, plot, MemoryValues, Color.FromArgb(51, 111, 194)); Series(g, plot, DiskValues, Color.FromArgb(193, 122, 24));
        }
        static void Series(Graphics g, Rectangle plot, IList<double?> values, Color color)
        {
            using (var line = new Pen(color, 1.6f))
                for (int i = 1; i < values.Count; i++)
                    if (values[i - 1].HasValue && values[i].HasValue)
                        g.DrawLine(line, plot.Left + (i - 1) * plot.Width / (float)Math.Max(14, values.Count - 1), plot.Bottom - (float)values[i - 1].Value / 100 * plot.Height,
                            plot.Left + i * plot.Width / (float)Math.Max(14, values.Count - 1), plot.Bottom - (float)values[i].Value / 100 * plot.Height);
        }
    }
    public sealed partial class MainForm : Form
    {
        static readonly Color Ink = Color.FromArgb(27, 43, 59), Muted = Color.FromArgb(92, 109, 124), Teal = Color.FromArgb(12, 128, 111), Canvas = Color.FromArgb(244, 247, 249);
        readonly IBackend backend; readonly IJournalStore journals; readonly bool demo;
        readonly UsageProfile profile = new UsageProfile();
        readonly Dictionary<string, ScanItem> selected = new Dictionary<string, ScanItem>();
        readonly Panel content = new Panel(), sidebar = new Panel();
        readonly Label status = new Label(); readonly List<Button> navigation = new List<Button>();
        readonly Button review = new Button();
        TableLayoutPanel pageLayout;
        ScanReport report; int page = 11; bool busy; CancellationTokenSource captureCancel;
        CaptureResult capture; CpuChart chart; Label captureStats;
        static readonly string[] Pages = { "Overview", "Usage profile", "Startup apps", "Scheduled tasks", "Optional services", "Installed apps", "Resource capture", "Recovery", "Device & storage", "Recommendations", "Help & tools", "Deep tune", "Desktop effects", "Running apps", "Tune result" };
        public MainForm(IBackend backend, IJournalStore journals, bool demo)
        {
            this.backend = backend; this.journals = journals; this.demo = demo; runningBackend = demo ? (IRunningApps)new DemoRunningApps() : new WindowsRunningApps();
            preferences = demo ? null : WorkspaceStore.Local(); workspace = preferences == null ? new UserWorkspace() : preferences.Load(); Profiles.Copy(workspace.Profile, profile);
            preferencesNotice = preferences == null ? null : preferences.Warning;
            Text = "Lean81" + (demo ? " — Demo" : "") + " · " + Product.Version;
            Font = new Font("Segoe UI", 9.5f); ForeColor = Ink; BackColor = Canvas;
            ClientSize = new Size(1080, 700); MinimumSize = new Size(980, 620); StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.White, Padding = new Padding(16, 10, 16, 10), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
            review.Text = "Review changes (0)"; review.Dock = DockStyle.Fill; review.Margin = Padding.Empty; StyleButton(review, true); review.Click += async (s, e) => await ReviewChanges(); footer.Controls.Add(review, 1, 0);
            status.Text = demo ? "Demo workspace · no system changes" : "Ready · scan to inspect this laptop"; status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.TextAlign = ContentAlignment.MiddleLeft; status.ForeColor = Muted; footer.Controls.Add(status, 0, 0);
            content.Dock = DockStyle.Fill; content.Padding = new Padding(28, 22, 28, 18); content.AutoScroll = true;
            sidebar.Dock = DockStyle.Left; sidebar.Width = 196; sidebar.BackColor = Ink;
            var branding = new Panel { Dock = DockStyle.Top, Height = 115 };
            var brand = new Label { Text = "Lean81", Font = new Font("Segoe UI", 25, FontStyle.Bold), ForeColor = Color.White, Bounds = new Rectangle(21, 21, 166, 49) }; branding.Controls.Add(brand);
            branding.Controls.Add(new Label { Text = "A lighter working day.", ForeColor = Color.FromArgb(175, 194, 207), Bounds = new Rectangle(24, 75, 170, 26) });
            var navPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; sidebar.Controls.Add(navPanel);
            for (int i = 0; i < Pages.Length; i++)
            {
                int target = i; var button = new Button { Text = Pages[i], UseMnemonic = false, Bounds = new Rectangle(8, i * 37, 160, 34), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(202, 215, 224), BackColor = Ink, TabIndex = i };
                button.FlatAppearance.BorderSize = 0; button.Click += (s, e) => ShowPage(target); navigation.Add(button); navPanel.Controls.Add(button);
            }
            var mode = new Label { Text = demo ? "SYNTHETIC DEMO\nNo system writes" : backend.IsAdministrator ? "ADMINISTRATOR\nChanges enabled" : "INSPECTION MODE\nNo admin needed to scan", ForeColor = Color.FromArgb(162, 184, 198), AutoSize = false, Height = 55, Dock = DockStyle.Bottom, Padding = new Padding(24, 4, 8, 6) }; sidebar.Controls.Add(mode);
            sidebar.Controls.Add(branding);
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            sidebar.Dock = DockStyle.Fill; sidebar.Margin = Padding.Empty; content.Margin = Padding.Empty;
            shell.Controls.Add(sidebar, 0, 0); shell.SetRowSpan(sidebar, 2); shell.Controls.Add(content, 1, 0); shell.Controls.Add(footer, 1, 1); Controls.Add(shell);
            FormClosing += (s, e) => { if (busy) { closeWhenIdle = true; e.Cancel = true; if (captureCancel != null) captureCancel.Cancel(); status.Text = "Finishing the current operation before closing…"; } };
            Shown += async (s, e) => await Scan(); ShowPage(page);
        }
        static Label LabelAt(string text, float size, FontStyle style = FontStyle.Regular)
        { return new Label { Text = text, AutoSize = false, ForeColor = style == FontStyle.Bold ? Ink : Muted, Font = new Font("Segoe UI", size, style), Dock = DockStyle.Top }; }
        static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(206, 216, 223); button.BackColor = primary ? Teal : Color.White; button.ForeColor = primary ? Color.White : Ink; button.Cursor = Cursors.Hand; button.Height = 36;
        }
        static Button ActionButton(string text, EventHandler action, bool primary = false)
        {
            var b = new Button { Text = text, UseMnemonic = false, AutoSize = true, MinimumSize = new Size(140, 36), Margin = new Padding(0, 0, 12, 0), Padding = new Padding(10, 0, 10, 0) };
            StyleButton(b, primary); b.Click += action; return b;
        }
        void Heading(string title, string subtitle)
        {
            var sub = LabelAt(subtitle, 10); sub.Height = 48; sub.Padding = new Padding(0, 3, 0, 0);
            var heading = LabelAt(title, 24, FontStyle.Bold); heading.Height = 48;
            pageLayout.Controls.Add(heading, 0, 0); pageLayout.Controls.Add(sub, 0, 1);
        }
        void Body(Control control) { control.Dock = DockStyle.Fill; control.Margin = Padding.Empty; pageLayout.Controls.Add(control, 0, 2); }
        static void Rows(Panel host, Control[] controls, params float[] heights)
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = controls.Length, Margin = Padding.Empty, Padding = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < controls.Length; i++)
            {
                table.RowStyles.Add(new RowStyle(heights[i] < 0 ? SizeType.Percent : SizeType.Absolute, heights[i] < 0 ? 100 : heights[i]));
                controls[i].Dock = DockStyle.Fill; controls[i].Margin = Padding.Empty;
                table.Controls.Add(controls[i], 0, i);
            }
            host.Controls.Add(table);
        }
        void ShowPage(int index)
        {
            page = index;
            foreach (Control child in content.Controls.Cast<Control>().ToArray()) child.Dispose();
            content.Controls.Clear(); chart = null; captureStats = null;
            pageLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            pageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); pageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); content.Controls.Add(pageLayout);
            for (int i = 0; i < navigation.Count; i++) { navigation[i].BackColor = i == page ? Color.FromArgb(44, 68, 85) : Ink; navigation[i].ForeColor = i == page ? Color.White : Color.FromArgb(202, 215, 224); }
            var navigationHost = navigation[index].Parent as ScrollableControl;
            if (navigationHost != null) navigationHost.ScrollControlIntoView(navigation[index]);
            if (index == 0) Overview(); else if (index == 1) Profile(); else if (index >= 2 && index <= 4) Inventory(index == 2 ? "startup" : index == 3 ? "task" : "service"); else if (index == 5) Apps(); else if (index == 6) CapturePage(); else if (index == 7) Recovery(); else if (index == 8) DevicePage(); else if (index == 9) RecommendationsPage(); else if (index == 10) ToolsPage(); else if (index == 11) QuickTunePage(); else if (index == 12) Inventory("setting"); else if (index == 13) RunningAppsPage(); else SessionResultPage();
            UpdateSelection();
        }
        void SetBusy(bool value, string message)
        {
            busy = value; foreach (var nav in navigation) nav.Enabled = !value;
            content.Enabled = !value || captureCancel != null;
            review.Enabled = !value && !choicesStale && selected.Count > 0; status.Text = message + (!value && preferencesNotice != null ? " · preferences unavailable" : "");
            if (!value && closeWhenIdle && IsHandleCreated) BeginInvoke(new Action(Close));
        }
        async Task<bool> Scan()
        {
            if (busy) return false; bool complete = false; SetBusy(true, "Scanning startup entries, optional services, tasks and installed apps…");
            try { report = await Task.Run(() => backend.Scan(profile)); device = await Task.Run(() => demo ? DeviceSnapshot.Demo() : DeviceSnapshot.Read()); runningApps = await Task.Run(() => runningBackend.Scan()); choicesStale = false; selected.Clear(); ShowPage(page); SetBusy(false, "Scan complete · " + report.Items.Count + " items · " + report.Notes.Count + " notes" + (preferences != null && preferences.Warning != null ? " · preferences reset" : "")); complete = true; }
            catch (Exception ex) { SetBusy(false, "Scan could not finish"); Error(ex); }
            UpdateSelection(); return complete;
        }
        static DataGridView Grid()
        {
            var grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false, EditMode = DataGridViewEditMode.EditOnEnter };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(233, 239, 243); grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink; grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            grid.ColumnHeadersHeight = 34; grid.RowTemplate.Height = 35; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.GridColor = Color.FromArgb(235, 239, 242);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 240, 236); grid.DefaultCellStyle.SelectionForeColor = Ink; grid.DefaultCellStyle.Padding = new Padding(5, 0, 2, 0);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 252); return grid;
        }
        void Inventory(string kind)
        {
            Heading(kind == "startup" ? "Start fewer things at sign-in." : kind == "task" ? "Review scheduled activity." : kind == "setting" ? "A simpler desktop." : "Keep the services you need.",
                choicesStale ? "Your usage answers changed. Rescan before selecting changes." : kind == "service" ? "Optional services only. Your usage profile and service dependencies decide which can be selected." : kind == "setting" ? "Reduce individual Explorer effects. Sign out and back in after applying; each original value can be restored." : "Select only items you recognize. Protected entries stay available for inspection.");
            var body = new Panel(); Body(body);
            var detail = new TextBox { Dock = DockStyle.Bottom, Height = 100, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Muted, ScrollBars = ScrollBars.Vertical, Text = "Select a row to inspect its purpose and tradeoff." }; body.Controls.Add(detail);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 80, WrapContents = true };
            toolbar.Controls.Add(new Label { Text = "Filter", AutoSize = true, Padding = new Padding(0, 8, 8, 0) });
            var search = new TextBox { Width = 160, Margin = new Padding(0, 4, 12, 0) }; toolbar.Controls.Add(search);
            var selectable = new CheckBox { Text = "Selectable only", AutoSize = true, Margin = new Padding(0, 7, 12, 0) }; toolbar.Controls.Add(selectable);
            toolbar.Controls.Add(ActionButton("Rescan", async (s, e) => await Scan()));
            if (kind == "task") toolbar.Controls.Add(ActionButton("Task Scheduler", (s, e) => OpenSystem("taskschd.msc", "")));
            if (kind == "startup") toolbar.Controls.Add(ActionButton("Startup folder", (s, e) => OpenStartup()));
            if (kind == "service") toolbar.Controls.Add(ActionButton("Select unused services", (s, e) => { if (report == null || choicesStale) { status.Text = "Rescan after changing your usage answers."; return; } foreach (var item in report.Items.Where(i => i.Kind == "service" && i.CanChange && i.Suggested)) selected[item.Id] = item; ShowPage(4); status.Text = "Unused services selected for review · no settings changed"; }));
            body.Controls.Add(toolbar);
            var grid = Grid(); grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Select", HeaderText = "", Width = 35, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            grid.Columns.Add("Name", "Name"); grid.Columns.Add("Scope", "Scope"); grid.Columns.Add("Status", "Current state"); grid.Columns.Add("Reason", "Why / next step"); grid.Columns["Name"].FillWeight = 30; grid.Columns["Scope"].FillWeight = 12; grid.Columns["Status"].FillWeight = 24; grid.Columns["Reason"].FillWeight = 34;
            for (int i = 1; i < grid.Columns.Count; i++) grid.Columns[i].ReadOnly = true;
            Action populate = () =>
            {
                grid.Rows.Clear(); if (report == null) return;
                foreach (var item in report.Items.Where(x => (x.Kind == kind || kind == "startup" && x.Kind == "startupfile") && (!selectable.Checked || !choicesStale && x.CanChange) && (x.Name + " " + x.Before.Target).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).OrderByDescending(x => x.CanChange).ThenBy(x => x.Name))
                {
                    string reason = choicesStale ? "Rescan after changing usage answers" : Profiles.FeatureReason(item, profile);
                    var row = grid.Rows[grid.Rows.Add(selected.ContainsKey(item.Id), item.Name, item.Scope, item.Status, reason)]; row.Tag = item; row.Cells[0].ReadOnly = choicesStale || !item.CanChange; row.Cells[4].ToolTipText = reason; row.Cells[1].ToolTipText = item.Name;
                    if (!item.CanChange) row.DefaultCellStyle.ForeColor = Muted;
                    row.Cells[0].ToolTipText = item.CanChange ? "Select for review" : "Kept, protected, missing or already disabled";
                }
                grid.ClearSelection(); if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
            };
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return; var row = grid.Rows[e.RowIndex]; var item = row.Tag as ScanItem; if (item == null) return;
                if (!choicesStale && item.CanChange && Convert.ToBoolean(row.Cells[0].Value)) selected[item.Id] = item; else selected.Remove(item.Id); UpdateSelection();
            };
            grid.SelectionChanged += (s, e) =>
            {
                if (grid.SelectedRows.Count == 0) return; var item = grid.SelectedRows[0].Tag as ScanItem; if (item == null) return;
                detail.Text = item.Before.Target + "  ·  " + item.Status + Environment.NewLine + Profiles.FeatureReason(item, profile) + ". " + item.Detail + Environment.NewLine + "Tradeoff: " + item.Tradeoff
                    + (item.Kind == "startup" && item.Before.Value != null ? Environment.NewLine + "Command (never executed here): " + Product.Clean(item.Before.Value, 1000) : "");
            };
            search.TextChanged += (s, e) => populate(); selectable.CheckedChanged += (s, e) => populate(); Rows(body, new Control[] { toolbar, grid, detail }, 80, -1, 100); populate();
        }
        Action<bool> captureControls;
        async Task StartCapture(int seconds)
        {
            if (busy) return; workspace.Workload = Product.Clean(workspace.Workload, 80).Trim(); if (workspace.Workload.Length == 0) workspace.Workload = "Normal laptop use"; SaveWorkspace(); capture = null; chart.Values.Clear(); chart.MemoryValues.Clear(); chart.DiskValues.Clear(); chart.Invalidate(); captureCancel = new CancellationTokenSource();
            SetBusy(true, "Capturing CPU, memory and disk activity…"); captureControls(true);
            var progress = new Progress<CaptureSample>(sample =>
            {
                if (chart == null || chart.IsDisposed) return; double? cpu = ResourceAnalysis.Number(sample.metrics, "cpu_pct", 100);
                chart.Values.Add(cpu); chart.Invalidate(); var top = sample.processes.FirstOrDefault();
                var available = ResourceAnalysis.Number(sample.metrics, "memory_available_pct", 100);
                chart.MemoryValues.Add(available.HasValue ? (double?)(100 - available.Value) : null); chart.DiskValues.Add(ResourceAnalysis.Number(sample.metrics, "disk_busy_pct", 100));
                captureStats.Text = ResourceAnalysis.Format(ResourceAnalysis.Number(sample.metrics, "cpu_pct", 100), "% CPU") + "  ·  " + ResourceAnalysis.Format(available, "% RAM available") + "  ·  " + ResourceAnalysis.Format(ResourceAnalysis.Number(sample.metrics, "disk_busy_pct", 100), "% disk busy") + "  ·  " + sample.t.ToString("0", CultureInfo.InvariantCulture) + "s";
                status.Text = top == null ? "Collecting resource observations…" : "CPU leader: " + top.name + " · " + top.cpu_pct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            });
            try
            {
                capture = demo ? CaptureResult.Demo() : await Task.Run(() => CpuCapture.Run(seconds, captureCancel.Token, sample => ((IProgress<CaptureSample>)progress).Report(sample)));
                if (capture.Samples.Count > 0) { latestSummary = CaptureSummary.From(capture, workspace.Workload, CaptureMachineKey); workspace.Add(latestSummary); SaveWorkspace(); }
            }
            catch (Exception ex) { Error(ex); }
            finally { captureCancel.Dispose(); captureCancel = null; SetBusy(false, "Capture finished · no system settings changed"); ShowPage(6); }
        }
        void ShowCaptureStats()
        {
            if (captureStats == null || capture == null) return;
            var values = capture.Samples.Where(s => s.metrics["cpu_pct"] != null).Select(s => Convert.ToDouble(s.metrics["cpu_pct"], CultureInfo.InvariantCulture)).ToArray();
            double weighted = capture.Samples.Where(s => s.metrics["cpu_pct"] != null).Sum(s => Convert.ToDouble(s.metrics["cpu_pct"], CultureInfo.InvariantCulture) * s.dt), observed = capture.Samples.Where(s => s.metrics["cpu_pct"] != null).Sum(s => s.dt);
            captureStats.Text = values.Length == 0 ? "No valid samples collected." : "Average " + (weighted / observed).ToString("0.0", CultureInfo.InvariantCulture) + "%  ·  Peak " + values.Max().ToString("0.0", CultureInfo.InvariantCulture) + "%  ·  " + capture.Samples.Count + " samples" + (capture.Synthetic ? "  ·  SYNTHETIC" : "") + (capture.Interrupted ? "  ·  stopped early" : "");
            if (capture.Error != null) captureStats.Text += "  ·  " + capture.Error;
        }
        void ExportCapture()
        {
            if (capture == null || capture.Samples.Count == 0) return;
            using (var options = new Form { Text = "Export resource trace", ClientSize = new Size(480, 180), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = Font })
            {
                var text = new Label { Text = "The trace includes CPU, memory, disk counters and process IDs, memory and I/O. Process I/O includes network and devices. No paths or startup commands are exported.", Bounds = new Rectangle(20, 17, 440, 51) }; options.Controls.Add(text);
                var names = new CheckBox { Text = "Include process names in this export", Bounds = new Rectangle(20, 78, 430, 25) }; options.Controls.Add(names);
                var save = ActionButton("Choose file…", (s, e) => options.DialogResult = DialogResult.OK, true); save.Bounds = new Rectangle(310, 124, 150, 36); options.Controls.Add(save); options.AcceptButton = save;
                if (options.ShowDialog(this) != DialogResult.OK) return;
                using (var dialog = new SaveFileDialog { Filter = "JSONL resource trace (*.jsonl)|*.jsonl|Resource sample spreadsheet (*.csv)|*.csv", FileName = "lean81-resources-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), AddExtension = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) try { if (dialog.FilterIndex == 2) capture.ExportCsv(dialog.FileName, names.Checked); else capture.Export(dialog.FileName, names.Checked); status.Text = "Capture exported · " + capture.Samples.Count + " samples"; } catch (Exception ex) { Error(ex); }
            }
        }
        void Recovery()
        {
            Heading("Every change has a way back.", "Restore saved settings after checking that their current state still matches the receipt.");
            var body = new Panel(); Body(body);
            if (!backend.IsAdministrator)
            {
                var note = LabelAt("Recovery receipts are private to administrators and SYSTEM. Enable changes to read or restore them.\n\nKeep the Lean81 folder in ProgramData: it holds the original settings needed for recovery.", 11); note.Dock = DockStyle.Top; note.Height = 100; body.Controls.Add(note);
                var enable = ActionButton("Enable changes…", (s, e) => Elevate(), true); Rows(body, new Control[] { note, enable }, -1, 40); return;
            }
            var grid = Grid(); grid.ReadOnly = true; grid.Columns.Add("Date", "Created (UTC)"); grid.Columns.Add("Count", "Items"); grid.Columns.Add("Status", "Status"); grid.Columns.Add("Id", "Receipt");
            grid.Columns[0].FillWeight = 32; grid.Columns[1].FillWeight = 10; grid.Columns[1].MinimumWidth = 60; grid.Columns[2].FillWeight = 22; grid.Columns[3].FillWeight = 36;
            var detail = new TextBox { Dock = DockStyle.Bottom, Height = 100, Multiline = true, ReadOnly = true, BackColor = Color.White, ScrollBars = ScrollBars.Vertical }; body.Controls.Add(detail);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
            actions.Controls.Add(ActionButton("Restore selected receipt…", async (s, e) =>
            {
                if (busy || grid.SelectedRows.Count == 0) return; var receipt = grid.SelectedRows[0].Tag as ChangeReceipt; if (receipt == null || receipt.Status == "Restored") return;
                if (!ReviewDialog("Restore saved settings", receipt.Entries.Select(x => x.Name + "\r\nOriginal: " + Policy.StateLabel(x.Original)).ToArray(), "Lean81 will restore the exact saved settings. If an item changed independently, recovery stops before writing.")) return;
                SetBusy(true, "Checking recovery states…");
                try { var result = await Task.Run(() => new ChangeEngine(backend, journals).Restore(receipt.Id, null)); selected.Clear(); ShowPage(7); SetBusy(false, "Recovery: " + result.Status); }
                catch (Exception ex) { SetBusy(false, "Recovery stopped · settings were not overwritten"); Error(ex); }
            }, true));
            actions.Controls.Add(ActionButton("Refresh", (s, e) => { if (!busy) ShowPage(7); })); body.Controls.Add(actions);
            actions.Controls.Add(ActionButton("Check saved states", async (s, e) =>
            {
                if (busy || grid.SelectedRows.Count == 0) return; var receipt = grid.SelectedRows[0].Tag as ChangeReceipt; if (receipt == null) return;
                SetBusy(true, "Checking saved states without changing anything…");
                try { var checks = await Task.Run(() => new ChangeEngine(backend, journals).Inspect(receipt.Id)); detail.Text = String.Join(Environment.NewLine, checks.Select(c => c.Name + " · " + c.Status + " · current: " + c.Current + " · saved: " + c.Original)); }
                catch (Exception ex) { Error(ex); } finally { SetBusy(false, "Recovery state check complete · no settings changed"); }
            }));
            actions.Controls.Add(ActionButton("Restore tune group…", async (s, e) =>
            {
                if (busy || grid.SelectedRows.Count == 0) return; var receipt = grid.SelectedRows[0].Tag as ChangeReceipt;
                if (receipt == null || receipt.GroupId == null) { status.Text = "Select a receipt created by Deep tune to restore its complete group."; return; }
                var group = journals.List().Where(r => r.GroupId == receipt.GroupId).ToArray();
                if (!ReviewDialog("Restore tune group", group.SelectMany(r => r.Entries).Select(x => x.Name + "\r\nOriginal: " + Policy.StateLabel(x.Original)).ToArray(), "Restore all settings from this tune, with service providers restored before their dependents. Independent edits stop the group check. Closed apps must be reopened manually.")) return;
                SetBusy(true, "Checking all tune-group recovery states…");
                try { int count = await Task.Run(() => new DeepTuneEngine(backend, journals).Restore(receipt.GroupId, null)); await RefreshAfterAction(); ShowPage(7); SetBusy(false, "Tune group restored · " + count + " settings"); }
                catch (Exception ex) { SetBusy(false, "Group recovery stopped; inspect saved states before continuing."); Error(ex); }
            }));
            try
            {
                foreach (var receipt in journals.List()) { var row = grid.Rows[grid.Rows.Add(receipt.CreatedUtc.Replace("T", " ").Substring(0, 19), receipt.Entries.Count, receipt.Status, receipt.Id.Substring(0, 12))]; row.Tag = receipt; }
            }
            catch (Exception ex) { detail.Text = "Receipts unavailable: " + Product.Clean(ex.Message, 400); }
            grid.SelectionChanged += (s, e) => { if (grid.SelectedRows.Count == 0) return; var r = grid.SelectedRows[0].Tag as ChangeReceipt; if (r != null) detail.Text = String.Join(Environment.NewLine, r.Entries.Select(x => x.Name + " · " + x.Status + (x.Error == null ? "" : " · " + x.Error))); };
            grid.ClearSelection(); if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
            Rows(body, new Control[] { grid, detail, actions }, -1, 100, 88);
        }
        bool ReviewDialog(string title, string[] entries, string explanation, string screenshot = null, string actionText = null)
        {
            using (var dialog = new Form { Text = title, ClientSize = new Size(690, 470), MinimumSize = new Size(620, 400), StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = Canvas, MinimizeBox = false, MaximizeBox = false, Padding = new Padding(20) })
            {
                var header = new Label { Text = explanation, Dock = DockStyle.Top, Height = 70, ForeColor = Muted };
                var list = new TextBox { Text = String.Join("\r\n\r\n", entries), Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = Color.White, ScrollBars = ScrollBars.Vertical };
                var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
                var apply = ActionButton(actionText ?? (title.StartsWith("Restore", StringComparison.Ordinal) ? "Restore settings" : "Apply selected changes"), (s, e) => dialog.DialogResult = DialogResult.OK, true);
                var cancel = ActionButton("Cancel", (s, e) => dialog.DialogResult = DialogResult.Cancel); controls.Controls.Add(apply); controls.Controls.Add(cancel);
                if (screenshot == null) controls.Controls.Add(ActionButton("Save review…", (s, e) => { using (var save = new SaveFileDialog { Filter = "Plain text review (*.txt)|*.txt", FileName = "lean81-review.txt" }) if (save.ShowDialog(dialog) == DialogResult.OK) try { File.WriteAllText(save.FileName, "Lean81 " + Product.Version + " · " + title + Environment.NewLine + explanation + Environment.NewLine + Environment.NewLine + String.Join(Environment.NewLine + Environment.NewLine, entries), new System.Text.UTF8Encoding(false)); } catch (Exception ex) { Error(ex); } }));
                var panel = new Panel { Dock = DockStyle.Fill }; Rows(panel, new Control[] { header, list, controls }, 70, -1, 55); dialog.Controls.Add(panel); dialog.CancelButton = cancel;
                dialog.Shown += (s, e) => { list.SelectionLength = 0; cancel.Focus(); };
                using (var timer = new System.Windows.Forms.Timer { Interval = 150 })
                {
                    if (screenshot != null)
                    {
                        timer.Tick += (s, e) => { timer.Stop(); using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png); } dialog.DialogResult = DialogResult.Cancel; };
                        dialog.Shown += (s, e) => timer.Start();
                    }
                    return dialog.ShowDialog(this) == DialogResult.OK;
                }
            }
        }
        async Task ReviewChanges()
        {
            if (busy || choicesStale || selected.Count == 0) return;
            var items = selected.Values.ToList();
            if (!ReviewDialog("Review selected changes", items.Select(x => x.Name + "  ·  " + x.Scope + "\r\n" + x.Before.Target + "\r\nTradeoff: " + x.Tradeoff).ToArray(), demo ? "DEMO: these changes only affect synthetic in-memory settings." : backend.IsAdministrator ? "Review every tradeoff. Original settings are saved before writing, and each selection is rechecked against current settings." : "Inspect or save this review without admin rights. Enable changes opens a fresh administrator scan; your usage answers are remembered and items must be selected again.", null, backend.IsAdministrator ? "Apply selected changes" : "Enable changes & rescan")) return;
            if (!backend.IsAdministrator) { SaveWorkspace(); Elevate(); return; }
            SetBusy(true, "Saving recovery data and applying selected changes…");
            try
            {
                var receipt = await Task.Run(() => new ChangeEngine(backend, journals).Apply(items, profile, null)); selected.Clear();
                report = await Task.Run(() => backend.Scan(profile)); ShowPage(7); SetBusy(false, "Changes: " + receipt.Status + " · receipt " + receipt.Id.Substring(0, 12));
                if (receipt.Status != "Applied") MessageBox.Show(this, "Changes stopped. Review the receipt and its error in Recovery before continuing.", "Review required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { SetBusy(false, "Changes stopped · review the current settings and recovery receipts"); Error(ex); }
        }
        void UpdateSelection() { review.Text = "Review changes (" + selected.Count + ")"; review.Enabled = !busy && !choicesStale && selected.Count > 0; }
        void Elevate()
        {
            if (demo) return;
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--admin-user " + WindowsIdentity.GetCurrent().User.Value) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) }); Close();
            }
            catch (System.ComponentModel.Win32Exception ex) { if (ex.NativeErrorCode == 1223) status.Text = "Administrator request cancelled. Inspection is still available."; else Error(ex); }
        }
        void OpenSystem(string file, string args)
        {
            if (demo) { status.Text = "Demo: Windows tools are available in normal mode."; return; }
            try
            {
                string path = Path.Combine(Environment.SystemDirectory, file);
                if (file.EndsWith(".msc", StringComparison.OrdinalIgnoreCase)) Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "mmc.exe"), "\"" + path + "\"") { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true });
            }
            catch (Exception ex) { Error(ex); }
        }
        void OpenStartup()
        {
            if (demo) { status.Text = "Demo: startup folders are inspection-only in normal mode."; return; }
            try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\"") { UseShellExecute = true }); } catch (Exception ex) { Error(ex); }
        }
        void Error(Exception ex) { MessageBox.Show(this, Product.Clean(ex.Message, 700), "Lean81", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        void ShowScanNotes()
        {
            using (var dialog = new Form { Text = "Scan notes", ClientSize = new Size(650, 410), StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = Canvas, Padding = new Padding(20), MinimizeBox = false, MaximizeBox = false })
            {
                var text = new TextBox { Multiline = true, ReadOnly = true, BackColor = Color.White, ScrollBars = ScrollBars.Vertical,
                    Text = report == null ? "Scan this laptop to see inventory notes." : String.Join("\r\n\r\n", report.Notes) };
                var close = ActionButton("Close", (s, e) => dialog.Close());
                var panel = new Panel { Dock = DockStyle.Fill }; Rows(panel, new Control[] { text, close }, -1, 40); dialog.Controls.Add(panel); dialog.CancelButton = close; dialog.ShowDialog(this);
            }
        }
        internal void SmokePage(int index)
        {
            capture = CaptureResult.Demo();
            if (workspace.History.Count == 0)
            {
                var before = CaptureSummary.From(capture, "Browsing (demo)", CaptureMachineKey); workspace.Add(before); workspace.BaselineId = before.Id;
                var after = CaptureResult.Demo(); foreach (var sample in after.Samples) sample.metrics["cpu_pct"] = Convert.ToDouble(sample.metrics["cpu_pct"], CultureInfo.InvariantCulture) * 0.8;
                latestSummary = CaptureSummary.From(after, "Browsing (demo)", CaptureMachineKey); workspace.Add(latestSummary);
            }
            if (index == 4) { profile.HomeGroup = profile.MediaSharing = profile.OfflineFiles = false; report = backend.Scan(profile); }
            if (index == 7 && journals.List().Count == 0)
            {
                var item = report.Items.First(x => x.Kind == "startup" && x.CanChange);
                new ChangeEngine(backend, journals).Apply(new[] { item }, profile, null);
            }
            ShowPage(index);
        }
        internal bool SmokeReady { get { return report != null && !busy; } }
        internal int SmokePageCount { get { return Pages.Length; } }
        internal void SmokeReviewScreenshot(string path)
        {
            if (!demo) throw new InvalidOperationException("GUI smoke requires synthetic mode.");
            if (ReviewDialog("Review selected changes", new[] { "Cloud sync companion  ·  user\r\nTradeoff: the app will no longer start automatically. Manual launch stays available.", "HomeGroup listener  ·  machine\r\nTradeoff: HomeGroup sharing features will be unavailable." }, "DEMO: these changes only affect synthetic in-memory settings.", path)) throw new InvalidOperationException("Smoke review must cancel.");
        }
        internal void ValidateSmokeLayout()
        {
            PerformLayout();
            if (pageLayout.GetControlFromPosition(0, 0).Height < 40 || pageLayout.GetControlFromPosition(0, 2).Height < 200 || !review.Visible || review.Height < 25)
                throw new InvalidOperationException("Required page content or review action is clipped.");
            CheckTables(this);
        }
        static void CheckTables(Control parent)
        {
            var table = parent as TableLayoutPanel;
            if (table != null)
            {
                var children = table.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
                for (int i = 0; i < children.Length; i++)
                {
                    if (!table.ClientRectangle.Contains(children[i].Bounds)) throw new InvalidOperationException("A table control is outside its visible area.");
                    for (int j = i + 1; j < children.Length; j++)
                        if (children[i].Bounds.IntersectsWith(children[j].Bounds)) throw new InvalidOperationException("Required controls overlap.");
                }
            }
            foreach (Control child in parent.Controls) CheckTables(child);
        }
        internal void SaveScreenshot(string path)
        {
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        }
    }
}
