using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Lean81
{
    public sealed class TuneSessionRequest
    {
        public DeepTunePlan Settings;
        public IList<RunningApp> Apps = new List<RunningApp>();
        public bool Force, Measure = true;
    }
    public sealed class TuneSessionResult
    {
        public string Status = "Applied", Error;
        public DateTime Started = DateTime.Now;
        public DeepTuneResult Settings;
        public bool Restored, SettingsStarted;
        public CaptureResult Before, After;
        public readonly List<AppCloseResult> Apps = new List<AppCloseResult>();
        public readonly List<string> Notes = new List<string>();
        public string Summary()
        {
            return Status + " · " + (Settings == null && SettingsStarted ? "settings need review" : (Settings == null ? 0 : Settings.Applied) + " settings changed") + " · "
                + Apps.Count(a => a.Closed) + " processes closed / already exited · " + Apps.Count(a => !a.Closed) + " kept / still running";
        }
        public string Details()
        {
            var lines = new List<string> { Summary(), "Started " + Started.ToString("g", CultureInfo.CurrentCulture) };
            if (Before != null && Before.Synthetic || After != null && After.Synthetic) lines.Add("SYNTHETIC DEMO · invented observations; no Windows changes.");
            if (Error != null) lines.Add("Stopped: " + Error);
            if (Restored) lines.Add("The settings in this result have been restored. These observations describe the earlier tune.");
            lines.Add(TuneReadings.NextSteps(After));
            if (Settings != null)
            {
                lines.Add("Recovery group: " + Settings.GroupId);
                foreach (var entry in Settings.Receipts.SelectMany(r => r.Entries)) lines.Add(entry.Name + " [" + entry.Original.Kind + "]: " + entry.Status + (entry.Error == null ? "" : " · " + entry.Error));
            }
            foreach (var app in Apps) lines.Add(app.Name + " / " + app.Pid + ": " + app.Status);
            lines.AddRange(Notes);
            lines.Add("Startup changes affect future launches. Desktop effects may need sign-out. Recovery restores saved settings; reopen closed apps manually. Short observations vary with workload and are not a speed benchmark or proof of a tune's effect.");
            return String.Join(Environment.NewLine + Environment.NewLine, lines);
        }
    }
    public sealed class TuneSessionEngine
    {
        readonly IBackend backend; readonly IJournalStore journals; readonly IRunningApps apps;
        readonly Func<CancellationToken, CaptureResult> observe;
        readonly Action<CancellationToken> settle;
        public TuneSessionEngine(IBackend backend, IJournalStore journals, IRunningApps apps,
            Func<CancellationToken, CaptureResult> observe, Action<CancellationToken> settle)
        { this.backend = backend; this.journals = journals; this.apps = apps; this.observe = observe; this.settle = settle; }
        public static IList<RunningApp> SuggestApps(IList<RunningApp> current, UsageProfile profile)
        {
            var result = new List<RunningApp>();
            foreach (var app in current ?? new RunningApp[0])
            {
                try { AppCatalog.Validate(app); if (!profile.Uses(AppCatalog.Match(app.Path).Feature)) result.Add(Copy(app)); }
                catch (InvalidOperationException) { }
            }
            return result.Take(128).ToList();
        }
        public static RunningApp Copy(RunningApp a)
        { return new RunningApp { Pid = a.Pid, Started = a.Started, Memory = a.Memory, Session = a.Session, Name = a.Name, Path = a.Path, Owner = a.Owner, Family = a.Family, Feature = a.Feature, Foreground = a.Foreground }; }
        static void Validate(TuneSessionRequest request)
        {
            if (request == null || request.Apps == null || request.Settings == null || request.Settings.Profile == null || request.Settings.Items.Count > 128
                || request.Apps.Count > 128 || request.Settings.Items.Count + request.Apps.Count == 0 || request.Apps.Any(a => a == null)
                || request.Apps.Select(a => a.Pid).Distinct().Count() != request.Apps.Count) throw new InvalidOperationException("Prepare a fresh, nonempty tune review.");
            foreach (var app in request.Apps) AppCatalog.Validate(app);
        }
        CaptureResult Observe(CancellationToken token, TuneSessionResult result)
        {
            token.ThrowIfCancellationRequested();
            try { return observe(token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Notes.Add("Resource readings unavailable: " + Product.Clean(ex.Message, 240)); return null; }
        }
        public TuneSessionResult Run(TuneSessionRequest request, CancellationToken token, Action<string> progress)
        {
            Validate(request);
            var result = new TuneSessionResult(); bool actionsStarted = false;
            // App preflight is before any setting writes. Native closure rechecks again
            // after the settings pass, using an open process handle and creation time.
            try
            {
                if (request.Measure) { if (progress != null) progress("Checking CPU, RAM and disk before tuning · about 8 seconds…"); result.Before = Observe(token, result); }
                token.ThrowIfCancellationRequested();
                var chosen = request.Apps.Select(Copy).ToList();
                var current = chosen.Count == 0 ? new List<RunningApp>() : apps.Scan();
                var ready = new List<RunningApp>();
                foreach (var app in chosen)
                {
                    var now = current.FirstOrDefault(a => a.Pid == app.Pid);
                    if (now == null || now.Started != app.Started || now.Path != app.Path || now.Owner != app.Owner || now.Session != app.Session || now.Family != app.Family || now.Foreground)
                    { result.Apps.Add(new AppCloseResult { Pid = app.Pid, Name = app.Name, Status = "Changed / unavailable at preflight; kept" }); continue; }
                    AppCatalog.Validate(now); ready.Add(app);
                }
                token.ThrowIfCancellationRequested();
                if (request.Settings.Items.Count != 0)
                {
                    if (progress != null) progress("Applying reviewed settings and saving their recovery group…");
                    actionsStarted = result.SettingsStarted = true;
                    result.Settings = new DeepTuneEngine(backend, journals).Apply(request.Settings, progress);
                    if (result.Settings.Status != "Applied") { result.Status = "Needs review"; result.Notes.Add("App closure was skipped because the settings pass did not finish. Inspect Recovery before trying again."); return result; }
                }
                foreach (var app in ready.OrderBy(a => System.IO.Path.GetFileName(a.Path).IndexOf("helper", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0))
                {
                    token.ThrowIfCancellationRequested();
                    if (progress != null) progress("Closing reviewed companion: " + app.Name);
                    actionsStarted = true;
                    result.Apps.Add(AppCatalog.CloseSelected(apps, new[] { app }, request.Force).Single());
                }
                if (request.Measure)
                {
                    if (progress != null) progress("Letting activity settle, then checking CPU, RAM and disk · about 10 seconds…");
                    settle(token); result.After = Observe(token, result);
                }
            }
            catch (OperationCanceledException) { result.Status = actionsStarted ? "Stopped after partial work" : "Stopped before changes"; result.Notes.Add("Remaining work was cancelled. Completed settings remain saved in Recovery; closed apps stay closed."); }
            catch (Exception ex) { result.Status = "Needs review"; result.Error = Product.Clean(ex.Message, 500); result.Notes.Add("Remaining actions were skipped. Inspect Recovery for earlier settings before retrying."); }
            return result;
        }
    }
    public sealed class TuneMetric
    {
        public string Name, Before, After, Difference;
    }
    public static class TuneReadings
    {
        static double? Value(CaptureResult capture, string key, double maximum)
        {
            if (capture == null || capture.Interrupted || capture.Error != null || capture.Samples == null) return null;
            double total = 0, known = 0, weighted = 0; int count = 0;
            foreach (var sample in capture.Samples)
            {
                if (sample == null || !Product.Finite(sample.dt) || sample.dt <= 0) return null;
                total += sample.dt; var number = ResourceAnalysis.Number(sample.metrics, key, maximum);
                if (number.HasValue) { known += sample.dt; weighted += sample.dt * number.Value; count++; }
            }
            return count >= 3 && total >= 6 && known >= 6 && known / total >= 0.8 && Product.Finite(total) && Product.Finite(weighted) ? (double?)(weighted / known) : null;
        }
        static string Power(CaptureResult capture)
        {
            if (capture == null || capture.Samples == null) return null;
            var values = capture.Samples.Select(s => s != null && s.metrics != null && s.metrics.ContainsKey("power") ? Convert.ToString(s.metrics["power"], CultureInfo.InvariantCulture) : "unknown").Distinct().ToArray();
            return values.Length == 1 && (values[0] == "ac" || values[0] == "battery") ? values[0] : null;
        }
        public static string NextSteps(CaptureResult after)
        {
            if (after == null) return "No after-tune observation. Action results remain available; resource pressure is unknown.";
            var lines = new List<string>(); var cpu = Value(after, "cpu_pct", 100); var available = Value(after, "memory_available_pct", 100); var disk = Value(after, "disk_busy_pct", 100); var commit = Value(after, "commit_pct", 100);
            if (cpu >= 80) lines.Add("CPU remains busy in the short after-check. Inspect Remaining load. Close unused browser tabs or review the workload; a demanding browser session can exceed an older CPU's capacity.");
            if (available < 15) lines.Add("Available RAM remains low. Save work and close unused tabs or apps. The Remaining load tab includes memory-heavy processes even if their CPU is idle.");
            if (commit >= 90) lines.Add("Commit is near its limit. Save work and close unused apps. Keep Windows' paging file enabled.");
            if (disk >= 80) lines.Add("Aggregate disk activity remains high. Remaining load shows process I/O, which also includes network/device operations and cannot identify the disk culprit by itself. Startup changes take effect at future launches.");
            if (cpu < 80 && available >= 15 && disk < 80 && commit < 90) lines.Add("No sustained resource pressure in this short after-check. This can miss spikes and does not establish a speed increase.");
            if (!cpu.HasValue || !available.HasValue || !disk.HasValue || !commit.HasValue) lines.Add("Some after-check resource readings are missing or incomplete. Their pressure remains unknown.");
            return String.Join(Environment.NewLine + Environment.NewLine, lines);
        }
        public static IList<TuneMetric> Compare(CaptureResult before, CaptureResult after)
        {
            var rows = new List<TuneMetric>(); string context = null;
            if (before == null || after == null) context = "Both readings needed";
            else if (before.Synthetic != after.Synthetic) context = "Demo / real readings differ";
            else if (Power(before) == null || Power(before) != Power(after)) context = "Power changed / unknown";
            foreach (var metric in new[] { new[] { "CPU", "cpu_pct", "%", "100", "1" }, new[] { "Available RAM", "memory_available_bytes", " MiB", "0", "1048576" }, new[] { "Disk busy (all disks)", "disk_busy_pct", "%", "100", "1" }, new[] { "Commit", "commit_pct", "%", "100", "1" } })
            {
                double max = metric[3] == "0" ? Double.MaxValue : 100, divisor = Double.Parse(metric[4], CultureInfo.InvariantCulture);
                var a = Value(before, metric[1], max); var b = Value(after, metric[1], max);
                string change = context ?? (!a.HasValue || !b.HasValue ? "Insufficient readings" : Math.Abs(b.Value - a.Value) / divisor < 0.05 ? "No sampled change" : (Math.Abs(b.Value - a.Value) / divisor).ToString("0.0", CultureInfo.InvariantCulture) + (metric[2] == "%" ? " points " : " MiB ") + (b.Value > a.Value ? "higher" : "lower"));
                rows.Add(new TuneMetric { Name = metric[0], Before = ResourceAnalysis.Format(a, metric[2], divisor), After = ResourceAnalysis.Format(b, metric[2], divisor), Difference = change });
            }
            return rows;
        }
    }
}
