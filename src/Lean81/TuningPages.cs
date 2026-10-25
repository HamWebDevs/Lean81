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
        string lastTune;
        void QuickTunePage()
        {
            Heading("Deep tune this laptop.", "Choose what stays. Lean81 stops unused service groups and prevents recognized background software from returning.");
            var body = new Panel(); Body(body);
            var list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White, Padding = new Padding(14, 8, 8, 8) };
            var checks = FeatureChecks(list, profile); var toolbar = new FlowLayoutPanel(); var presets = Presets(checks); toolbar.Controls.Add(presets);
            toolbar.Controls.Add(ActionButton("Maximum reduction", (s, e) => presets.SelectedIndex = 5, true));
            var choices = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var effects = new CheckBox { Text = "Reduce desktop effects", AutoSize = true, Checked = true };
            var startup = new CheckBox { Text = "Include recognized startup apps, shortcuts and third-party tasks for unused categories", AutoSize = true, Checked = true };
            var power = new CheckBox { Text = "Select High performance plan (mains required; stays active until changed)", AutoSize = true };
            choices.Controls.Add(effects); choices.Controls.Add(startup); choices.Controls.Add(power);
            var note = new TextBox { Text = lastTune ?? "Maximum reduction assumes no printing, Bluetooth, indexing, preloading, TV, sync, chat or local game launchers. Check anything you need. Windows security, networking and core services stay protected. App updates you disable will need manual attention. No recording or export is required.", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
            var actions = new FlowLayoutPanel { WrapContents = true };
            actions.Controls.Add(ActionButton("Review deep tune…", async (s, e) =>
            {
                if (busy) return;
                bool includeEffects = effects.Checked, includeStartup = startup.Checked, includePower = power.Checked;
                Profiles.Copy(ReadChecks(checks), profile); SaveWorkspace(); bool scanned = await Scan();
                if (!scanned) return;
                try
                {
                    var plan = DeepTunePlan.Build(report, profile, includeEffects, includePower, includeStartup);
                    if (plan.Items.Count == 0) { status.Text = "No matching enabled settings. Try Maximum reduction or review Startup apps."; return; }
                    var entries = plan.Items.Select(i => i.Name + "  ·  " + i.Scope + " / " + i.Kind + "\r\n" + i.Tradeoff).Concat(plan.Kept.Select(k => "KEPT: " + k)).ToArray();
                    if (!ReviewDialog("Review deep tune", entries, "Services are stopped in dependency order, then kept disabled. Startup changes prevent future launches; close running apps separately. Every settings batch belongs to one restore group.", null, backend.IsAdministrator ? "Apply deep tune" : "Enable changes & rescan")) return;
                    if (!backend.IsAdministrator) { Elevate(); return; }
                    SetBusy(true, "Applying reviewed deep tune…");
                    var progress = new Progress<string>(message => status.Text = message);
                    var result = await Task.Run(() => new DeepTuneEngine(backend, journals).Apply(plan, message => ((IProgress<string>)progress).Report(message)));
                    lastTune = result.Status + " · " + result.Applied + " settings changed in " + result.Receipts.Count + " recovery batches. Use Recovery → Restore tune group to undo them. Open Running apps to close selected companions now.";
                    selected.Clear(); await RefreshAfterAction(); ShowPage(11); SetBusy(false, lastTune);
                }
                catch (Exception ex) { SetBusy(false, "Tune stopped. Earlier changes, if any, are saved in Recovery."); Error(ex); }
            }, true));
            actions.Controls.Add(ActionButton("Close background apps", (s, e) => ShowPage(13)));
            actions.Controls.Add(ActionButton("Recovery", (s, e) => ShowPage(7)));
            Rows(body, new Control[] { toolbar, list, choices, note, actions }, 42, -1, 78, 70, 48);
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
