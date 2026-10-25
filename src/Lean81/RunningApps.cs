using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Lean81
{
    public sealed class AppFamily
    {
        public string Id, Name, Feature, Folder;
        public string[] Files;
        public AppFamily(string id, string name, string feature, string folder, params string[] files) { Id = id; Name = name; Feature = feature; Folder = folder; Files = files; }
    }
    public sealed class RunningApp
    {
        public int Pid; public long Started, Memory; public uint Session;
        public string Name, Path, Owner, Family, Feature;
        public bool Foreground;
    }
    public sealed class AppCloseResult { public int Pid; public string Name, Status; public bool Closed; }
    public interface IRunningApps
    {
        IList<RunningApp> Scan();
        AppCloseResult Close(RunningApp approved, bool force);
    }
    public static class AppCatalog
    {
        public static readonly AppFamily[] Families = {
            new AppFamily("onedrive", "OneDrive", "sync", @"\Microsoft\OneDrive\", "OneDrive.exe"),
            new AppFamily("dropbox", "Dropbox", "sync", @"\Dropbox\", "Dropbox.exe"),
            new AppFamily("steam", "Steam", "launchers", @"\Steam\", "steam.exe", "steamwebhelper.exe"),
            new AppFamily("epic", "Epic Games launcher", "launchers", @"\Epic Games\", "EpicGamesLauncher.exe", "EpicWebHelper.exe"),
            new AppFamily("gog", "GOG Galaxy", "launchers", @"\GOG Galaxy\", "GalaxyClient.exe", "GalaxyClient Helper.exe", "GalaxyCommunication.exe"),
            new AppFamily("discord", "Discord", "chat", @"\Discord\", "Discord.exe"),
            new AppFamily("slack", "Slack", "chat", @"\slack\", "slack.exe"),
            new AppFamily("teams", "Teams", "chat", @"\Microsoft\Teams\", "Teams.exe"),
            new AppFamily("skype", "Skype", "chat", @"\Skype\", "Skype.exe"),
            new AppFamily("spotify", "Spotify", "music", @"\Spotify\", "Spotify.exe"),
            new AppFamily("adobe", "Adobe Creative Cloud", "updaters", @"\Adobe\", "Creative Cloud.exe", "CCXProcess.exe", "CoreSync.exe", "Adobe Desktop Service.exe")
        };
        public static AppFamily Match(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length > 32768 || path.StartsWith(@"\\", StringComparison.Ordinal) || !System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:\\")) return null;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase) || Policy.IsProtectedText(path)) return null;
            string file = System.IO.Path.GetFileName(path);
            return Families.FirstOrDefault(f => path.IndexOf(f.Folder, StringComparison.OrdinalIgnoreCase) >= 0 && f.Files.Contains(file, StringComparer.OrdinalIgnoreCase));
        }
        public static void Validate(RunningApp app)
        {
            var family = app == null ? null : Match(app.Path);
            if (family == null || family.Id != app.Family || app.Pid <= 4 || app.Started <= 0 || String.IsNullOrEmpty(app.Owner) || new[] { "S-1-5-18", "S-1-5-19", "S-1-5-20" }.Contains(app.Owner) || app.Foreground) throw new InvalidOperationException("This is not an approved background companion. Browsers, Windows and foreground apps are kept.");
        }
        public static IList<AppCloseResult> CloseSelected(IRunningApps backend, IList<RunningApp> selected, bool force)
        {
            if (selected == null || selected.Count == 0 || selected.Count > 128 || selected.Select(a => a.Pid).Distinct().Count() != selected.Count) throw new InvalidOperationException("Select a bounded list of recognized background apps.");
            foreach (var app in selected) Validate(app);
            var result = new List<AppCloseResult>();
            foreach (var app in selected.OrderBy(a => System.IO.Path.GetFileName(a.Path).IndexOf("helper", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0))
            {
                try { result.Add(backend.Close(app, force)); }
                catch (Exception ex) { result.Add(new AppCloseResult { Pid = app.Pid, Name = app.Name, Status = Product.Clean(ex.Message, 180) }); }
            }
            return result;
        }
    }
    sealed class AppProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public AppProcessHandle() : base(true) { }
        protected override bool ReleaseHandle() { return WindowsRunningApps.CloseHandle(handle); }
    }
    public sealed class WindowsRunningApps : IRunningApps
    {
        readonly string user = WindowsIdentity.GetCurrent().User.Value;
        readonly uint session;
        [DllImport("kernel32.dll", SetLastError = true)] static extern AppProcessHandle OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(AppProcessHandle handle, out long created, out long exited, out long kernel, out long users);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(AppProcessHandle handle, uint flags, StringBuilder name, ref uint length);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ProcessIdToSessionId(uint pid, out uint value);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsProcessCritical(AppProcessHandle handle, out bool critical);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(AppProcessHandle handle, uint code);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(AppProcessHandle handle, uint milliseconds);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(AppProcessHandle process, uint access, out AppProcessHandle token);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        public WindowsRunningApps()
        {
            using (var self = Process.GetCurrentProcess()) if (!ProcessIdToSessionId((uint)self.Id, out session)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        RunningApp Read(AppProcessHandle handle, int pid)
        {
            long started, exited, kernel, users; bool critical; uint processSession;
            if (!GetProcessTimes(handle, out started, out exited, out kernel, out users) || !IsProcessCritical(handle, out critical) || critical || !ProcessIdToSessionId((uint)pid, out processSession) || processSession != session) return null;
            uint length = 32768; var path = new StringBuilder((int)length);
            if (!QueryFullProcessImageName(handle, 0, path, ref length)) return null;
            var family = AppCatalog.Match(path.ToString()); if (family == null) return null;
            AppProcessHandle token;
            if (!OpenProcessToken(handle, 8, out token)) return null;
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            {
                if (identity.User.Value != user) return null;
                long memory = 0; try { using (var p = Process.GetProcessById(pid)) memory = Math.Max(0, p.WorkingSet64); } catch { }
                return new RunningApp { Pid = pid, Started = started, Path = path.ToString(), Owner = user, Session = processSession, Family = family.Id, Name = family.Name, Feature = family.Feature, Memory = memory };
            }
        }
        string ForegroundFamily()
        {
            uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            using (var handle = OpenProcess(0x1000, false, (int)pid))
            {
                if (handle.IsInvalid) return null;
                var app = Read(handle, (int)pid); return app == null ? null : app.Family;
            }
        }
        public IList<RunningApp> Scan()
        {
            var result = new List<RunningApp>(); string foreground = ForegroundFamily();
            var processes = Process.GetProcesses();
            var names = new HashSet<string>(AppCatalog.Families.SelectMany(f => f.Files).Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in processes.Take(1024))
                {
                    try
                    {
                        if (!names.Contains(p.ProcessName)) continue;
                        using (var handle = OpenProcess(0x1000, false, p.Id))
                        {
                            if (handle.IsInvalid) continue;
                            var app = Read(handle, p.Id); if (app != null) { app.Foreground = app.Family == foreground; result.Add(app); }
                        }
                    }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                    if (result.Count >= 256) break;
                }
            }
            finally { foreach (var p in processes) p.Dispose(); }
            return result;
        }
        public AppCloseResult Close(RunningApp approved, bool force)
        {
            AppCatalog.Validate(approved);
            using (var handle = OpenProcess(0x1000 | 0x100000 | (force ? 1u : 0u), false, approved.Pid))
            {
                if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); return new AppCloseResult { Pid = approved.Pid, Name = approved.Name, Closed = error == 87, Status = error == 87 ? "Already exited" : "No longer accessible; no action sent" }; }
                var current = Read(handle, approved.Pid);
                if (current == null || current.Started != approved.Started || current.Path != approved.Path || current.Owner != approved.Owner || current.Session != approved.Session || ForegroundFamily() == current.Family)
                    throw new InvalidOperationException("The app identity or foreground state changed. Refresh before closing it.");
                try { using (var p = Process.GetProcessById(approved.Pid)) p.CloseMainWindow(); } catch (InvalidOperationException) { }
                if (WaitForSingleObject(handle, 700) == 0) return new AppCloseResult { Pid = approved.Pid, Name = approved.Name, Closed = true, Status = "Closed" };
                if (!force) return new AppCloseResult { Pid = approved.Pid, Name = approved.Name, Status = "Still running; force-close option is available after review" };
                if (ForegroundFamily() == current.Family) throw new InvalidOperationException("This app moved to the foreground; it was kept.");
                if (!TerminateProcess(handle, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                bool exited = WaitForSingleObject(handle, 1500) == 0;
                return new AppCloseResult { Pid = approved.Pid, Name = approved.Name, Closed = exited, Status = exited ? "Force closed" : "Exit requested; still finishing I/O" };
            }
        }
    }
    public sealed class DemoRunningApps : IRunningApps
    {
        readonly List<RunningApp> apps = new List<RunningApp> {
            new RunningApp { Pid = 301, Started = 1, Name = "Steam", Path = @"C:\Program Files (x86)\Steam\steam.exe", Family = "steam", Feature = "launchers", Owner = "demo", Memory = 310L << 20 },
            new RunningApp { Pid = 302, Started = 2, Name = "OneDrive", Path = @"C:\Users\Demo\AppData\Local\Microsoft\OneDrive\OneDrive.exe", Family = "onedrive", Feature = "sync", Owner = "demo", Memory = 110L << 20 },
            new RunningApp { Pid = 303, Started = 3, Name = "Discord", Path = @"C:\Users\Demo\AppData\Local\Discord\app\Discord.exe", Family = "discord", Feature = "chat", Owner = "demo", Memory = 230L << 20, Foreground = true }
        };
        public IList<RunningApp> Scan() { return apps.ToList(); }
        public AppCloseResult Close(RunningApp approved, bool force)
        {
            AppCatalog.Validate(approved); apps.RemoveAll(a => a.Pid == approved.Pid);
            return new AppCloseResult { Pid = approved.Pid, Name = approved.Name, Closed = true, Status = "Demo: closed in memory only" };
        }
    }
}
