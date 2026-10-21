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
        public readonly List<double> Values = new List<double>();
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
            var points = Values.Select((v, i) => new PointF(plot.Left + i * plot.Width / (float)Math.Max(14, Values.Count - 1), plot.Bottom - (float)Math.Max(0, Math.Min(100, v)) / 100 * plot.Height)).ToArray();
            var area = new List<PointF> { new PointF(points[0].X, plot.Bottom) }; area.AddRange(points); area.Add(new PointF(points[points.Length - 1].X, plot.Bottom));
            using (var fill = new SolidBrush(Color.FromArgb(30, 15, 145, 125))) g.FillPolygon(fill, area.ToArray());
            using (var line = new Pen(Color.FromArgb(13, 137, 119), 2.5f)) g.DrawLines(line, points);
        }
    }
    public sealed class MainForm : Form
    {
        static readonly Color Ink = Color.FromArgb(27, 43, 59), Muted = Color.FromArgb(92, 109, 124), Teal = Color.FromArgb(12, 128, 111), Canvas = Color.FromArgb(244, 247, 249);
        readonly IBackend backend; readonly IJournalStore journals; readonly bool demo;
        readonly UsageProfile profile = new UsageProfile();
        readonly Dictionary<string, ScanItem> selected = new Dictionary<string, ScanItem>();
        readonly Panel content = new Panel(), sidebar = new Panel();
        readonly Label status = new Label(); readonly List<Button> navigation = new List<Button>();
        readonly Button review = new Button();
        TableLayoutPanel pageLayout;
        ScanReport report; int page; bool busy; CancellationTokenSource captureCancel;
        CaptureResult capture; CpuChart chart; Label captureStats;
        static readonly string[] Pages = { "Overview", "Usage profile", "Startup apps", "Scheduled tasks", "Optional services", "Installed apps", "CPU capture", "Recovery" };
        public MainForm(IBackend backend, IJournalStore journals, bool demo)
        {
            this.backend = backend; this.journals = journals; this.demo = demo;
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
            var brand = new Label { Text = "Lean81", Font = new Font("Segoe UI", 25, FontStyle.Bold), ForeColor = Color.White, Bounds = new Rectangle(21, 21, 166, 49) }; sidebar.Controls.Add(brand);
            sidebar.Controls.Add(new Label { Text = "A lighter working day.", ForeColor = Color.FromArgb(175, 194, 207), Bounds = new Rectangle(24, 75, 170, 26) });
            for (int i = 0; i < Pages.Length; i++)
            {
                int target = i; var button = new Button { Text = Pages[i], Bounds = new Rectangle(12, 130 + i * 43, 172, 38), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(202, 215, 224), BackColor = Ink, TabIndex = i };
                button.FlatAppearance.BorderSize = 0; button.Click += (s, e) => ShowPage(target); navigation.Add(button); sidebar.Controls.Add(button);
            }
            var mode = new Label { Text = demo ? "SYNTHETIC DEMO\nNo system writes" : backend.IsAdministrator ? "ADMINISTRATOR\nChanges enabled" : "INSPECTION MODE\nNo admin needed to scan", ForeColor = Color.FromArgb(162, 184, 198), AutoSize = false, Height = 55, Dock = DockStyle.Bottom, Padding = new Padding(24, 4, 8, 6) }; sidebar.Controls.Add(mode);
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            sidebar.Dock = DockStyle.Fill; sidebar.Margin = Padding.Empty; content.Margin = Padding.Empty;
            shell.Controls.Add(sidebar, 0, 0); shell.SetRowSpan(sidebar, 2); shell.Controls.Add(content, 1, 0); shell.Controls.Add(footer, 1, 1); Controls.Add(shell);
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; if (captureCancel != null) captureCancel.Cancel(); status.Text = "Finishing the current operation before closing…"; } };
            Shown += async (s, e) => await Scan(); ShowPage(0);
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
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(140, 36), Margin = new Padding(0, 0, 12, 0), Padding = new Padding(10, 0, 10, 0) };
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
            if (index == 0) Overview(); else if (index == 1) Profile(); else if (index >= 2 && index <= 4) Inventory(index == 2 ? "startup" : index == 3 ? "task" : "service"); else if (index == 5) Apps(); else if (index == 6) CapturePage(); else Recovery();
            UpdateSelection();
        }
        void SetBusy(bool value, string message)
        {
            busy = value; foreach (var nav in navigation) nav.Enabled = !value;
            content.Enabled = !value || captureCancel != null;
            review.Enabled = !value && selected.Count > 0; status.Text = message;
        }
        async Task Scan()
        {
            if (busy) return; SetBusy(true, "Scanning startup entries, optional services, tasks and installed apps…");
            try { report = await Task.Run(() => backend.Scan(profile)); selected.Clear(); ShowPage(page); SetBusy(false, "Scan complete · " + report.Items.Count + " items · " + report.Notes.Count + " notes"); }
            catch (Exception ex) { SetBusy(false, "Scan could not finish"); Error(ex); }
            UpdateSelection();
        }
        void Overview()
        {
            Heading("Make room for what matters.", "Inspect background activity, choose what you use, then make reversible changes.");
            var body = new Panel(); Body(body);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, WrapContents = false };
            buttons.Controls.Add(ActionButton("Scan this laptop", async (s, e) => await Scan(), true));
            if (!backend.IsAdministrator && !demo) buttons.Controls.Add(ActionButton("Enable changes…", (s, e) => Elevate()));
            else buttons.Controls.Add(ActionButton("Set usage profile", (s, e) => ShowPage(1)));
            body.Controls.Add(buttons);
            var next = new Panel { Dock = DockStyle.Bottom, Height = 153, BackColor = Color.White, Padding = new Padding(18) };
            var hint = LabelAt("1   Scan your laptop\n2   Mark the features you don't use\n3   Select items and review their tradeoffs\n4   Capture CPU use before and after", 11); hint.Dock = DockStyle.Fill; next.Controls.Add(hint); body.Controls.Add(next);
            var notes = LabelAt(demo ? "Demo inventory is synthetic. You can try review and recovery without touching Windows." : "Changes stay optional. Security, networking and core Windows services are protected.\n\nLean81 does not run in the background after you close it. Recovery receipts stay on this laptop.", 10.5f); notes.Dock = DockStyle.Fill; notes.Padding = new Padding(2, 16, 0, 8); body.Controls.Add(notes);
            var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 124, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            string[] names = { "STARTUP ENTRIES", "OPTIONAL SERVICES", "SCHEDULED TASKS" }; string[] kinds = { "startup", "service", "task" };
            for (int i = 0; i < 3; i++)
            {
                var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, i < 2 ? 14 : 0, 0), Padding = new Padding(16, 12, 10, 8) };
                var caption = LabelAt(names[i], 8.5f); caption.Height = 23;
                var number = LabelAt(report == null ? "—" : report.Items.Count(x => x.Kind == kinds[i]).ToString(CultureInfo.InvariantCulture), 29, FontStyle.Bold); number.Height = 52;
                var foot = LabelAt("Found in this scan", 9); foot.Dock = DockStyle.Bottom; foot.Height = 22;
                card.Controls.Add(number); card.Controls.Add(caption); card.Controls.Add(foot); cards.Controls.Add(card, i, 0);
            }
            Rows(body, new Control[] { cards, notes, next, buttons }, 124, -1, 153, 52);
        }
        void Profile()
        {
            Heading("What do you use?", "Keep a feature checked if you use it or aren't sure. Nothing changes when you edit this profile.");
            var body = new Panel(); Body(body);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, WrapContents = false };
            actions.Controls.Add(ActionButton("Refresh recommendations", async (s, e) => { await Scan(); ShowPage(4); }, true)); body.Controls.Add(actions);
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White, Padding = new Padding(18, 12, 12, 10) }; body.Controls.Add(list);
            var labels = new[] { "Printing, including PDF / virtual printers", "Bluetooth devices", "Indexed file and email search", "Touch, pen, handwriting or the on-screen keyboard", "HomeGroup sharing", "Windows Media Player network sharing", "Offline network files", "Application preloading / Superfetch (may speed up launches)" };
            bool[] values = { profile.Printing, profile.Bluetooth, profile.IndexedSearch, profile.TouchKeyboard, profile.HomeGroup, profile.MediaSharing, profile.OfflineFiles, profile.Prefetch };
            for (int i = 0; i < labels.Length; i++)
            {
                int n = i; var check = new CheckBox { Text = labels[i], Checked = values[i], AutoSize = true, Margin = new Padding(0, 6, 0, 8), MinimumSize = new Size(600, 27) };
                check.CheckedChanged += (s, e) => { switch (n) { case 0: profile.Printing = check.Checked; break; case 1: profile.Bluetooth = check.Checked; break; case 2: profile.IndexedSearch = check.Checked; break; case 3: profile.TouchKeyboard = check.Checked; break; case 4: profile.HomeGroup = check.Checked; break; case 5: profile.MediaSharing = check.Checked; break; case 6: profile.OfflineFiles = check.Checked; break; case 7: profile.Prefetch = check.Checked; break; } selected.Clear(); UpdateSelection(); };
                list.Controls.Add(check);
            }
            Rows(body, new Control[] { list, actions }, -1, 48);
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
            Heading(kind == "startup" ? "Start fewer things at sign-in." : kind == "task" ? "Review scheduled activity." : "Keep the services you need.",
                kind == "service" ? "Optional services only. Your usage profile and service dependencies decide which can be selected." : "Select only items you recognize. Protected entries stay available for inspection.");
            var body = new Panel(); Body(body);
            var detail = new TextBox { Dock = DockStyle.Bottom, Height = 100, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Muted, ScrollBars = ScrollBars.Vertical, Text = "Select a row to inspect its purpose and tradeoff." }; body.Controls.Add(detail);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 47, WrapContents = false };
            toolbar.Controls.Add(new Label { Text = "Filter", AutoSize = true, Padding = new Padding(0, 8, 8, 0) });
            var search = new TextBox { Width = 215, Margin = new Padding(0, 4, 12, 0) }; toolbar.Controls.Add(search);
            toolbar.Controls.Add(ActionButton("Rescan", async (s, e) => await Scan()));
            if (kind == "task") toolbar.Controls.Add(ActionButton("Task Scheduler", (s, e) => OpenSystem("taskschd.msc", "")));
            if (kind == "startup") toolbar.Controls.Add(ActionButton("Startup folder", (s, e) => OpenStartup()));
            body.Controls.Add(toolbar);
            var grid = Grid(); grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Select", HeaderText = "", Width = 35, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            grid.Columns.Add("Name", "Name"); grid.Columns.Add("Scope", "Scope"); grid.Columns.Add("Status", "Current state"); grid.Columns["Name"].FillWeight = 54; grid.Columns["Scope"].FillWeight = 18; grid.Columns["Status"].FillWeight = 28;
            for (int i = 1; i < grid.Columns.Count; i++) grid.Columns[i].ReadOnly = true;
            Action populate = () =>
            {
                grid.Rows.Clear(); if (report == null) return;
                foreach (var item in report.Items.Where(x => x.Kind == kind && (x.Name + " " + x.Before.Target).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var row = grid.Rows[grid.Rows.Add(selected.ContainsKey(item.Id), item.Name, item.Scope, item.Status)]; row.Tag = item; row.Cells[0].ReadOnly = !item.CanChange;
                    if (!item.CanChange) row.DefaultCellStyle.ForeColor = Muted;
                    row.Cells[0].ToolTipText = item.CanChange ? "Select for review" : "Kept, protected, missing or already disabled";
                }
                grid.ClearSelection(); if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
            };
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return; var row = grid.Rows[e.RowIndex]; var item = row.Tag as ScanItem; if (item == null) return;
                if (item.CanChange && Convert.ToBoolean(row.Cells[0].Value)) selected[item.Id] = item; else selected.Remove(item.Id); UpdateSelection();
            };
            grid.SelectionChanged += (s, e) =>
            {
                if (grid.SelectedRows.Count == 0) return; var item = grid.SelectedRows[0].Tag as ScanItem; if (item == null) return;
                detail.Text = item.Before.Target + "  ·  " + item.Status + Environment.NewLine + item.Detail + Environment.NewLine + "Tradeoff: " + item.Tradeoff
                    + (item.Kind == "startup" && item.Before.Value != null ? Environment.NewLine + "Command (never executed here): " + Product.Clean(item.Before.Value, 1000) : "");
            };
            search.TextChanged += (s, e) => populate(); Rows(body, new Control[] { toolbar, grid, detail }, 47, -1, 100); populate();
        }
        void Apps()
        {
            Heading("Give unused apps a second look.", "Review software you recognize. Use the normal Windows uninstaller when you're ready.");
            var body = new Panel(); Body(body); var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
            actions.Controls.Add(ActionButton("Programs and Features", (s, e) => OpenSystem("control.exe", "appwiz.cpl"), true)); body.Controls.Add(actions);
            var grid = Grid(); grid.ReadOnly = true; grid.Columns.Add("Name", "Application"); grid.Columns.Add("Publisher", "Publisher"); grid.Columns.Add("Version", "Version"); grid.Columns.Add("Note", "Review note");
            grid.Columns[0].FillWeight = 30; grid.Columns[1].FillWeight = 22; grid.Columns[2].FillWeight = 12; grid.Columns[3].FillWeight = 36;
            if (report != null) foreach (var app in report.Apps.OrderBy(a => a.Name)) grid.Rows.Add(app.Name, app.Publisher, app.Version, app.Note);
            Rows(body, new Control[] { grid, actions }, -1, 48);
        }
        void CapturePage()
        {
            Heading("Find the source of CPU spikes.", "A short capture samples overall CPU and up to five busy processes every two seconds.");
            var body = new Panel(); Body(body);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, WrapContents = false };
            var duration = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(0, 6, 12, 0) }; duration.Items.AddRange(new object[] { "30 seconds", "60 seconds", "120 seconds" }); duration.SelectedIndex = 0; actions.Controls.Add(duration);
            var start = ActionButton("Start capture", async (s, e) => await StartCapture(duration.SelectedIndex == 0 ? 30 : duration.SelectedIndex == 1 ? 60 : 120), true); actions.Controls.Add(start);
            var stop = ActionButton("Stop", (s, e) => { if (captureCancel != null) captureCancel.Cancel(); }); stop.Enabled = false; actions.Controls.Add(stop);
            var export = ActionButton("Export trace…", (s, e) => ExportCapture()); export.Enabled = capture != null; actions.Controls.Add(export); body.Controls.Add(actions);
            captureStats = LabelAt("Ready to observe. Use the laptop normally during capture.", 11); captureStats.Dock = DockStyle.Top; captureStats.Height = 48; body.Controls.Add(captureStats);
            var note = LabelAt("CPU is normalized across all logical processors. Inaccessible and short-lived processes may be missed.\nExports omit process names by default and can be opened in ReclaimLab. No wake-delay measurements are collected.", 9); note.Dock = DockStyle.Bottom; note.Height = 58; body.Controls.Add(note);
            Label peaks = null;
            if (capture != null && capture.Samples.Count > 0)
            {
                var top = capture.Samples.SelectMany(s => s.processes).GroupBy(p => p.pid + "\n" + p.name).Select(g => g.OrderByDescending(p => p.cpu_pct).First()).OrderByDescending(p => p.cpu_pct).Take(3);
                peaks = LabelAt("Highest observed process CPU\n" + String.Join("    ·    ", top.Select(p => p.name + "  " + p.cpu_pct.ToString("0.0", CultureInfo.InvariantCulture) + "%")), 10);
                peaks.Dock = DockStyle.Bottom; peaks.Height = 58; body.Controls.Add(peaks);
            }
            chart = new CpuChart { Dock = DockStyle.Fill };
            if (peaks == null) Rows(body, new Control[] { captureStats, chart, note, actions }, 48, -1, 58, 50);
            else Rows(body, new Control[] { captureStats, chart, peaks, note, actions }, 48, -1, 58, 58, 50);
            if (capture != null) { chart.Values.AddRange(capture.Samples.Select(s => Convert.ToDouble(s.metrics["cpu_pct"] ?? 0, CultureInfo.InvariantCulture))); ShowCaptureStats(); }
            start.Tag = new Action<bool>(running => { start.Enabled = !running; stop.Enabled = running; duration.Enabled = !running; export.Enabled = !running && capture != null; });
            captureControls = (Action<bool>)start.Tag;
        }
        Action<bool> captureControls;
        async Task StartCapture(int seconds)
        {
            if (busy) return; capture = null; chart.Values.Clear(); chart.Invalidate(); captureCancel = new CancellationTokenSource();
            SetBusy(true, "Capturing CPU activity…"); captureControls(true);
            var progress = new Progress<CaptureSample>(sample =>
            {
                if (chart == null || chart.IsDisposed) return; double cpu = Convert.ToDouble(sample.metrics["cpu_pct"] ?? 0, CultureInfo.InvariantCulture);
                chart.Values.Add(cpu); chart.Invalidate(); var top = sample.processes.FirstOrDefault();
                captureStats.Text = cpu.ToString("0.0", CultureInfo.InvariantCulture) + "% CPU  ·  " + sample.t.ToString("0", CultureInfo.InvariantCulture) + "s  ·  " + (top == null ? "No accessible busy process" : top.name + " " + top.cpu_pct.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            });
            try { capture = demo ? CaptureResult.Demo() : await Task.Run(() => CpuCapture.Run(seconds, captureCancel.Token, sample => ((IProgress<CaptureSample>)progress).Report(sample))); }
            catch (Exception ex) { Error(ex); }
            finally { captureCancel.Dispose(); captureCancel = null; SetBusy(false, "Capture finished · no system settings changed"); ShowPage(6); }
        }
        void ShowCaptureStats()
        {
            if (captureStats == null || capture == null) return;
            var values = capture.Samples.Where(s => s.metrics["cpu_pct"] != null).Select(s => Convert.ToDouble(s.metrics["cpu_pct"], CultureInfo.InvariantCulture)).ToArray();
            captureStats.Text = values.Length == 0 ? "No valid samples collected." : "Average " + values.Average().ToString("0.0", CultureInfo.InvariantCulture) + "%  ·  Peak " + values.Max().ToString("0.0", CultureInfo.InvariantCulture) + "%  ·  " + capture.Samples.Count + " samples" + (capture.Synthetic ? "  ·  SYNTHETIC" : "") + (capture.Interrupted ? "  ·  stopped early" : "");
            if (capture.Error != null) captureStats.Text += "  ·  " + capture.Error;
        }
        void ExportCapture()
        {
            if (capture == null || capture.Samples.Count == 0) return;
            using (var options = new Form { Text = "Export CPU trace", ClientSize = new Size(480, 180), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = Font })
            {
                var text = new Label { Text = "The trace includes CPU, available memory, process IDs and working-set sizes. No paths or startup commands are exported.", Bounds = new Rectangle(20, 17, 440, 51) }; options.Controls.Add(text);
                var names = new CheckBox { Text = "Include process names in this export", Bounds = new Rectangle(20, 78, 430, 25) }; options.Controls.Add(names);
                var save = ActionButton("Choose file…", (s, e) => options.DialogResult = DialogResult.OK, true); save.Bounds = new Rectangle(310, 124, 150, 36); options.Controls.Add(save); options.AcceptButton = save;
                if (options.ShowDialog(this) != DialogResult.OK) return;
                using (var dialog = new SaveFileDialog { Filter = "ReclaimLab trace (*.jsonl)|*.jsonl", FileName = "lean81-cpu-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".jsonl" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) try { capture.Export(dialog.FileName, names.Checked); status.Text = "Trace exported · " + capture.Samples.Count + " samples"; } catch (Exception ex) { Error(ex); }
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
            try
            {
                foreach (var receipt in journals.List()) { var row = grid.Rows[grid.Rows.Add(receipt.CreatedUtc.Replace("T", " ").Substring(0, 19), receipt.Entries.Count, receipt.Status, receipt.Id.Substring(0, 12))]; row.Tag = receipt; }
            }
            catch (Exception ex) { detail.Text = "Receipts unavailable: " + Product.Clean(ex.Message, 400); }
            grid.SelectionChanged += (s, e) => { if (grid.SelectedRows.Count == 0) return; var r = grid.SelectedRows[0].Tag as ChangeReceipt; if (r != null) detail.Text = String.Join(Environment.NewLine, r.Entries.Select(x => x.Name + " · " + x.Status + (x.Error == null ? "" : " · " + x.Error))); };
            grid.ClearSelection(); if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
            Rows(body, new Control[] { grid, detail, actions }, -1, 100, 48);
        }
        bool ReviewDialog(string title, string[] entries, string explanation, string screenshot = null)
        {
            using (var dialog = new Form { Text = title, ClientSize = new Size(690, 470), MinimumSize = new Size(620, 400), StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = Canvas, MinimizeBox = false, MaximizeBox = false, Padding = new Padding(20) })
            {
                var header = new Label { Text = explanation, Dock = DockStyle.Top, Height = 70, ForeColor = Muted };
                var list = new TextBox { Text = String.Join("\r\n\r\n", entries), Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = Color.White, ScrollBars = ScrollBars.Vertical };
                var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
                var apply = ActionButton(title.StartsWith("Restore", StringComparison.Ordinal) ? "Restore settings" : "Apply selected changes", (s, e) => dialog.DialogResult = DialogResult.OK, true);
                var cancel = ActionButton("Cancel", (s, e) => dialog.DialogResult = DialogResult.Cancel); controls.Controls.Add(apply); controls.Controls.Add(cancel);
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
            if (busy || selected.Count == 0) return;
            if (!backend.IsAdministrator) { Elevate(); return; }
            var items = selected.Values.ToList();
            if (!ReviewDialog("Review selected changes", items.Select(x => x.Name + "  ·  " + x.Scope + "\r\n" + x.Before.Target + "\r\nTradeoff: " + x.Tradeoff).ToArray(), demo ? "DEMO: these changes only affect synthetic in-memory settings." : "Review every tradeoff. Lean81 saves a private recovery receipt before changing anything. Selections are rechecked against current settings.")) return;
            SetBusy(true, "Saving recovery data and applying selected changes…");
            try
            {
                var receipt = await Task.Run(() => new ChangeEngine(backend, journals).Apply(items, profile, null)); selected.Clear();
                report = await Task.Run(() => backend.Scan(profile)); ShowPage(7); SetBusy(false, "Changes: " + receipt.Status + " · receipt " + receipt.Id.Substring(0, 12));
                if (receipt.Status != "Applied") MessageBox.Show(this, "Changes stopped. Review the receipt and its error in Recovery before continuing.", "Review required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { SetBusy(false, "Changes stopped · review the current settings and recovery receipts"); Error(ex); }
        }
        void UpdateSelection() { review.Text = "Review changes (" + selected.Count + ")"; review.Enabled = !busy && selected.Count > 0; }
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
        internal void SmokePage(int index)
        {
            capture = CaptureResult.Demo();
            if (index == 4) { profile.HomeGroup = profile.MediaSharing = profile.OfflineFiles = false; report = backend.Scan(profile); }
            if (index == 7 && journals.List().Count == 0)
            {
                var item = report.Items.First(x => x.Kind == "startup" && x.CanChange);
                new ChangeEngine(backend, journals).Apply(new[] { item }, profile, null);
            }
            ShowPage(index);
        }
        internal bool SmokeReady { get { return report != null && !busy; } }
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
