using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lean81
{
    public static class TuningPlan
    {
        public static System.Collections.Generic.IList<ScanItem> Build(ScanReport report, UsageProfile profile, bool includeEffects)
        {
            var result = new System.Collections.Generic.List<ScanItem>(); if (report == null) return result;
            foreach (var item in report.Items.Where(i => i.CanChange && i.Before != null && i.Kind == i.Before.Kind && (i.Kind == "service" || includeEffects && i.Kind == "setting")))
            {
                try { Policy.Validate(item.Before, profile, false); if (item.Before.Exists && item.Before.Enabled) result.Add(item); } catch (InvalidOperationException) { }
            }
            if (result.Count > Product.MaxChanges) throw new InvalidOperationException("This review exceeds the supported batch size. Choose fewer settings.");
            return result;
        }
    }
    public sealed partial class MainForm
    {
        void QuickTunePage()
        {
            Heading("Less background. More breathing room.", "Keep features checked if you use them. Build a review of matching services, then apply when ready.");
            var body = new Panel(); Body(body);
            var list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White, Padding = new Padding(14, 8, 8, 8) };
            var checks = FeatureChecks(list, profile); var toolbar = new FlowLayoutPanel(); toolbar.Controls.Add(Presets(checks));
            var effects = new CheckBox { Text = "Also include the three desktop effects in my review", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            var note = new TextBox { Text = "Browser / cloud gaming is a starting point: check every answer. Untick indexed search or Superfetch only if you want to try those tradeoffs. Startup apps are chosen individually. Settings are backed up before changes; Recovery restores them. Effects may need sign-out. Services with enabled dependents stay protected.", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            var actions = new FlowLayoutPanel { WrapContents = true };
            actions.Controls.Add(ActionButton("Build my review…", async (s, e) =>
            {
                if (busy) return; bool includeEffects = effects.Checked;
                Profiles.Copy(ReadChecks(checks), profile); SaveWorkspace(); bool scanned = await Scan();
                if (!scanned || choicesStale || report == null || busy) return;
                selected.Clear();
                foreach (var item in TuningPlan.Build(report, profile, includeEffects)) selected[item.Id] = item;
                UpdateSelection();
                if (selected.Count == 0) { status.Text = "No matching enabled settings · adjust your answers or review Startup apps"; return; }
                await ReviewChanges();
            }, true));
            actions.Controls.Add(ActionButton("Review startup apps", (s, e) => ShowPage(2)));
            actions.Controls.Add(ActionButton("Desktop effects", (s, e) => ShowPage(12)));
            Rows(body, new Control[] { toolbar, list, effects, note, actions }, 42, -1, 36, 80, 48);
        }
        void ResourceDetails(TabPage pageTab)
        {
            var body = new Panel { Dock = DockStyle.Fill }; pageTab.Controls.Add(body);
            var facts = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            var last = capture == null ? null : capture.Samples.LastOrDefault();
            facts.Text = "Average RAM available: " + ResourceAnalysis.Format(ResourceAnalysis.Average(capture, "memory_available_pct", 100), "%") + "   ·   latest commit: " + ResourceAnalysis.Format(last == null ? null : ResourceAnalysis.Number(last.metrics, "commit_bytes"), " GiB", 1073741824) + " / " + ResourceAnalysis.Format(last == null ? null : ResourceAnalysis.Number(last.metrics, "commit_limit_bytes"), " GiB", 1073741824)
                + "\r\nAverage disk busy: " + ResourceAnalysis.Format(ResourceAnalysis.Average(capture, "disk_busy_pct", 100), "%") + "   ·   throughput: " + ResourceAnalysis.Format(ResourceAnalysis.Average(capture, "disk_bytes_per_sec"), " MiB/s", 1048576) + "   ·   queue: " + ResourceAnalysis.Format(ResourceAnalysis.Average(capture, "disk_queue"), "")
                + "\r\nPeak commit: " + ResourceAnalysis.Format(ResourceAnalysis.Peak(capture, "commit_pct", 100), "%") + "   ·   page-ins: " + ResourceAnalysis.Format(ResourceAnalysis.Average(capture, "pages_input_per_sec"), " pages/s") + "   ·   kernel CPU: " + ResourceAnalysis.Format(ResourceAnalysis.Average(capture, "kernel_cpu_pct", 100), "%")
                + "\r\nLatest kernel nonpaged / paged: " + ResourceAnalysis.Format(last == null ? null : ResourceAnalysis.Number(last.metrics, "kernel_nonpaged_bytes"), " MiB", 1048576) + " / " + ResourceAnalysis.Format(last == null ? null : ResourceAnalysis.Number(last.metrics, "kernel_paged_bytes"), " MiB", 1048576)
                + "   ·   cache: " + ResourceAnalysis.Format(last == null ? null : ResourceAnalysis.Number(last.metrics, "system_cache_bytes"), " MiB", 1048576);
            var toolbar = new FlowLayoutPanel(); toolbar.Controls.Add(new Label { Text = "Sort processes by", AutoSize = true, Padding = new Padding(0, 7, 10, 0) });
            var sort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(0, 4, 12, 0) }; sort.Items.AddRange(new object[] { "Peak CPU", "Working-set RAM", "Private memory", "Read/write I/O" }); sort.SelectedIndex = 0; toolbar.Controls.Add(sort);
            toolbar.Controls.Add(ActionButton("Resource Monitor", (s, e) => OpenTool("resources")));
            var grid = Grid(); grid.ReadOnly = true;
            foreach (string name in new[] { "Process / PID", "Peak CPU", "RAM MiB", "Private MiB", "I/O MiB/s", "Samples" }) grid.Columns.Add(name, name);
            grid.Columns[0].FillWeight = 28; for (int i = 1; i < grid.Columns.Count; i++) grid.Columns[i].FillWeight = i == 5 ? 10 : 15;
            Action populate = () =>
            {
                grid.Rows.Clear(); var processes = ResourceAnalysis.Processes(capture);
                var ordered = processes.OrderByDescending(p => sort.SelectedIndex == 1 ? p.PeakWorkingSet : sort.SelectedIndex == 2 ? p.PeakPrivate : sort.SelectedIndex == 3 ? (p.HasIo ? p.PeakIo : -1) : p.PeakCpu).ThenBy(p => p.Name);
                foreach (var p in ordered)
                {
                    var row = grid.Rows[grid.Rows.Add(p.Name + " / " + p.Pid, p.PeakCpu.ToString("0.0", CultureInfo.InvariantCulture) + "%", (p.PeakWorkingSet / 1048576).ToString("0.0", CultureInfo.InvariantCulture), (p.PeakPrivate / 1048576).ToString("0.0", CultureInfo.InvariantCulture), p.HasIo ? (p.PeakIo / 1048576).ToString("0.0", CultureInfo.InvariantCulture) : "Unknown", p.Samples.ToString(CultureInfo.InvariantCulture))];
                    row.Cells[0].ToolTipText = p.Name; row.Cells[4].ToolTipText = "Peak read/write I/O includes file, network and device operations. It is not per-process disk utilization.";
                }
            };
            sort.SelectedIndexChanged += (s, e) => populate(); populate();
            var findings = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Text = ResourceAnalysis.Findings(capture) };
            Rows(body, new Control[] { facts, toolbar, grid, findings }, 74, 44, -1, 100);
        }
        internal void SmokeResourcesScreenshot(string path)
        {
            SmokePage(6); var tabs = (TabControl)pageLayout.GetControlFromPosition(0, 2); tabs.SelectedIndex = 2; Refresh(); ValidateSmokeLayout(); SaveScreenshot(path);
        }
        internal void SmokeTuningInteractions()
        {
            if (!demo) throw new InvalidOperationException("Synthetic mode required.");
            selected.Clear(); ShowPage(11);
            var before = Product.Json().Serialize(profile); var presets = Descendants(content).OfType<ComboBox>().Single();
            presets.SelectedIndex = 4;
            var answers = ReadChecks(Descendants(content).OfType<CheckBox>().Take(FeatureLabels.Length).ToArray());
            if (Product.Json().Serialize(answers) != Product.Json().Serialize(Profiles.Preset(3)) || before != Product.Json().Serialize(profile) || selected.Count != 0)
                throw new InvalidOperationException("A quick-tune preset changed saved answers or applied changes without review.");
            ShowPage(12); var grid = Descendants(content).OfType<DataGridView>().Single();
            var row = grid.Rows.Cast<DataGridViewRow>().First(r => ((ScanItem)r.Tag).CanChange); row.Cells[0].Value = true;
            if (selected.Count != 1 || selected.Values.Single().Kind != "setting" || !review.Enabled) throw new InvalidOperationException("Desktop effects are not connected to review.");
            row.Cells[0].Value = false;
            SmokePage(6); ((TabControl)pageLayout.GetControlFromPosition(0, 2)).SelectedIndex = 2;
            var sort = Descendants(content).OfType<ComboBox>().Single(c => c.Items.Count == 4); sort.SelectedIndex = 1;
            grid = Descendants(content).OfType<DataGridView>().First(g => g.Columns.Count == 6);
            if (grid.Rows.Count == 0 || !Convert.ToString(grid.Rows[0].Cells[0].Value, CultureInfo.InvariantCulture).StartsWith("IdleMemoryApp", StringComparison.Ordinal)) throw new InvalidOperationException("Idle memory-heavy process is missing from resource sorting.");
            chart.Values[3] = null; chart.DiskValues[4] = null;
            using (var bitmap = new Bitmap(chart.Width, chart.Height)) chart.DrawToBitmap(bitmap, new Rectangle(Point.Empty, chart.Size));
        }
    }
}
