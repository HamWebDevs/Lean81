using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml;
using Microsoft.Win32;

namespace Lean81
{
    public sealed class WindowsBackend : IBackend
    {
        public string MachineScope { get { return Product.Hash(Environment.MachineName + "\n" + WindowsIdentity.GetCurrent().User.Value); } }
        public bool IsAdministrator { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
        public ScanReport Scan(UsageProfile profile)
        {
            var report = new ScanReport();
            foreach (string scope in Environment.Is64BitOperatingSystem ? new[] { "user", "machine32", "machine64" } : new[] { "user", "machine32" })
            {
                try
                {
                    using (var root = Root(scope))
                    using (var key = root.OpenSubKey(Product.RunKey))
                        if (key != null) foreach (var name in key.GetValueNames().Take(Product.MaxItems))
                        {
                            try { report.Items.Add(Make(Read(new ItemState { Kind = "startup", Scope = scope, Target = name }), name, "Starts at sign-in. Inspect the app before disabling it.", "The app will no longer start automatically. You can still launch it yourself.", profile)); }
                            catch (Exception ex) { Note(report, "Startup entry skipped", ex); }
                        }
                }
                catch (Exception ex) { Note(report, "Startup location unavailable", ex); }
                ScanApps(report, scope);
            }
            foreach (var option in Policy.Services)
            {
                try
                {
                    var state = NativeServices.Read(option.Name);
                    string detail = profile.Uses(option.Feature) ? "Kept by your usage profile." : "Marked unused in your profile. Review the tradeoff before selecting.";
                    if (state.Dependents.Length != 0) detail = "Kept: needed by " + String.Join(", ", state.Dependents);
                    report.Items.Add(Make(state, option.Title, detail, option.Tradeoff, profile));
                }
                catch (System.ComponentModel.Win32Exception ex) { if (ex.NativeErrorCode != 1060) Note(report, option.Name + " unavailable", ex); }
                catch (Exception ex) { Note(report, option.Name + " unavailable", ex); }
            }
            foreach (var option in DesktopSettings.Options)
            {
                try { report.Items.Add(Make(DesktopSettings.Read(new ItemState { Kind = "setting", Scope = "user", Target = option.Id }), option.Title, "Optional desktop effect. Changes take full effect after signing out and back in.", option.Tradeoff, profile)); }
                catch (Exception ex) { Note(report, option.Title + " unavailable", ex); }
            }
            try
            {
                dynamic scheduler = Scheduler();
                try { WalkTasks(scheduler.GetFolder(@"\"), report, profile, 0); }
                finally { Release(scheduler); }
            }
            catch (Exception ex) { Note(report, "Task inventory incomplete", ex); }
            report.Notes.Add("Startup folders and RunOnce keys are outside this version's change scope. Open startup folders to inspect their shortcuts.");
            report.Notes.Add("Installed apps are listed for manual review; Lean81 never executes uninstall strings.");
            return report;
        }
        static void Note(ScanReport report, string prefix, Exception ex)
        { if (report.Notes.Count < 30) report.Notes.Add(prefix + ": " + Product.Clean(ex.Message, 180)); }
        static ScanItem Make(ItemState state, string name, string detail, string tradeoff, UsageProfile profile)
        {
            bool allowed = state.Exists && state.Enabled;
            try { Policy.Validate(state, profile, false); } catch { allowed = false; }
            return new ScanItem { Id = Product.Hash(state.Identity()), Name = Product.Clean(name, 180), Kind = state.Kind, Scope = state.Scope,
                Status = Policy.StateLabel(state), Detail = detail, Tradeoff = tradeoff, CanChange = allowed,
                Suggested = allowed && state.Kind == "service", Before = state };
        }
        static RegistryKey Root(string scope)
        {
            return RegistryKey.OpenBaseKey(scope == "user" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                scope == "machine64" ? RegistryView.Registry64 : scope == "machine32" ? RegistryView.Registry32 : RegistryView.Default);
        }
        public ItemState Read(ItemState identity)
        {
            if (identity.Kind == "setting") return DesktopSettings.Read(identity);
            if (identity.Kind == "service") return NativeServices.Read(identity.Target);
            if (identity.Kind == "task")
            {
                dynamic scheduler = Scheduler(); dynamic task = null; dynamic folder = null;
                try
                {
                    folder = scheduler.GetFolder(@"\"); task = folder.GetTask(identity.Target);
                    return TaskState(task);
                }
                finally { Release(task); Release(folder); Release(scheduler); }
            }
            if (identity.Kind != "startup" || !new[] { "user", "machine32", "machine64" }.Contains(identity.Scope) || !Policy.ValidName(identity.Target) || identity.Target.Contains("\\") || identity.Target.Contains("/"))
                throw new InvalidOperationException("Unsupported startup identity.");
            using (var root = Root(identity.Scope))
            using (var key = root.OpenSubKey(Product.RunKey))
            {
                bool exists = key != null && key.GetValueNames().Contains(identity.Target, StringComparer.OrdinalIgnoreCase);
                var result = new ItemState { Kind = "startup", Target = identity.Target, Scope = identity.Scope, Exists = exists, Enabled = exists };
                if (exists)
                {
                    result.ValueKind = (int)key.GetValueKind(identity.Target);
                    result.Value = key.GetValue(identity.Target, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                }
                return result;
            }
        }
        public void Preflight(ItemState state, UsageProfile profile, bool restoring)
        {
            Policy.Validate(state, profile, restoring);
            if (!IsAdministrator) throw new InvalidOperationException("Administrator mode is required.");
            if (state.Kind == "setting") { DesktopSettings.CheckAccess(state); return; }
            if (state.Kind == "service")
            {
                var now = NativeServices.Read(state.Target);
                if (now.Definition != state.Definition) throw new InvalidOperationException("Service configuration identity changed.");
                if (!restoring && now.Dependents.Length > 0) throw new InvalidOperationException("A dependent service needs this service.");
                NativeServices.CheckAccess(state, restoring);
            }
            else if (state.Kind == "startup")
            {
                using (var root = Root(state.Scope))
                using (var key = root.OpenSubKey(Product.RunKey, true))
                    if (key == null) throw new InvalidOperationException("Startup key is missing; Lean81 will not recreate the location.");
            }
            else
            {
                dynamic scheduler = Scheduler(); dynamic folder = null; dynamic task = null;
                try
                {
                    folder = scheduler.GetFolder(@"\"); task = folder.GetTask(state.Target);
                    string xml = (string)task.Xml;
                    if (ProtectedTask(state.Target, xml)) throw new InvalidOperationException("System, security or hardware task is inspection-only.");
                    if (TaskDefinition(xml) != state.Definition) throw new InvalidOperationException("Task definition changed.");
                }
                finally { Release(task); Release(folder); Release(scheduler); }
            }
        }
        public void Disable(ItemState original) { Set(original, false); }
        public void Restore(ItemState original) { Set(original, true); }
        void Set(ItemState original, bool restore)
        {
            // The engine has just verified identity/state and saved intent. Revalidate the allowlist here too.
            Policy.Validate(original, new UsageProfile { Printing = false, Bluetooth = false, IndexedSearch = false, TouchKeyboard = false, HomeGroup = false, MediaSharing = false, OfflineFiles = false, Prefetch = false, Scanning = false, PortableDevices = false, ErrorReporting = false, LinkTracking = false }, restore);
            if (!IsAdministrator) throw new InvalidOperationException("Administrator mode is required.");
            if (original.Kind == "setting") { DesktopSettings.Set(original, restore); return; }
            if (original.Kind == "service") { NativeServices.Set(original, restore); return; }
            if (original.Kind == "startup")
            {
                using (var root = Root(original.Scope))
                using (var key = root.OpenSubKey(Product.RunKey, true))
                {
                    if (key == null) throw new InvalidOperationException("Startup key is missing.");
                    if (restore) key.SetValue(original.Target, original.Value, (RegistryValueKind)original.ValueKind);
                    else key.DeleteValue(original.Target, true);
                    key.Flush();
                }
                return;
            }
            dynamic scheduler = Scheduler(); dynamic folder = null; dynamic task = null;
            try
            {
                folder = scheduler.GetFolder(@"\"); task = folder.GetTask(original.Target);
                if (ProtectedTask(original.Target, (string)task.Xml)) throw new InvalidOperationException("Protected task.");
                task.Enabled = restore ? original.Enabled : false;
            }
            finally { Release(task); Release(folder); Release(scheduler); }
        }
        internal static dynamic Scheduler()
        {
            dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            try { scheduler.Connect(); return scheduler; } catch { Release(scheduler); throw; }
        }
        internal static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        static XmlDocument TaskDocument(string xml)
        {
            if (xml == null || xml.Length > 512 * 1024) throw new InvalidOperationException("Unsupported task definition size.");
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new System.IO.StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 512 * 1024 })) doc.Load(reader);
            return doc;
        }
        internal static string TaskDefinition(string xml)
        {
            var doc = TaskDocument(xml);
            var ns = new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
            var enabled = doc.SelectSingleNode("/t:Task/t:Settings/t:Enabled", ns);
            if (enabled != null) enabled.ParentNode.RemoveChild(enabled);
            return Product.Hash(doc.OuterXml);
        }
        static bool ProtectedTask(string path, string xml)
        {
            var doc = TaskDocument(xml);
            var author = doc.SelectSingleNode("/*[local-name()='Task']/*[local-name()='RegistrationInfo']/*[local-name()='Author']");
            return path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                || (author != null && author.InnerText.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
                || Policy.IsProtectedText(path + " " + doc.InnerText);
        }
        static ItemState TaskState(dynamic task)
        {
            return new ItemState { Kind = "task", Scope = "machine", Target = (string)task.Path, Exists = true,
                Enabled = (bool)task.Enabled, Definition = TaskDefinition((string)task.Xml) };
        }
        static void WalkTasks(dynamic folder, ScanReport report, UsageProfile profile, int depth)
        {
            dynamic tasks = null; dynamic folders = null;
            try
            {
                if (depth > 12 || report.Items.Count >= Product.MaxItems) { report.Notes.Add("Task scan reached the bounded inventory limit."); return; }
                tasks = folder.GetTasks(1);
                for (int i = 1; i <= (int)tasks.Count && report.Items.Count < Product.MaxItems; i++)
                {
                    dynamic task = null;
                    try
                    {
                        task = tasks.Item(i); var state = TaskState(task);
                        var item = Make(state, (string)task.Name, "Scheduled activity. Review its purpose in Task Scheduler before choosing.", "This task will no longer run automatically; app updates or maintenance may be affected.", profile);
                        if (ProtectedTask(state.Target, (string)task.Xml)) { item.CanChange = false; item.Detail = "Protected system, security or hardware task; inspection only."; }
                        report.Items.Add(item);
                    }
                    catch (Exception ex) { Note(report, "Task skipped", ex); }
                    finally { Release(task); }
                }
                folders = folder.GetFolders(0);
                for (int i = 1; i <= (int)folders.Count && report.Items.Count < Product.MaxItems; i++) WalkTasks(folders.Item(i), report, profile, depth + 1);
            }
            finally { Release(tasks); Release(folders); Release(folder); }
        }
        static void ScanApps(ScanReport report, string scope)
        {
            try
            {
                using (var root = Root(scope))
                using (var apps = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    if (apps != null) foreach (var name in apps.GetSubKeyNames().Take(1024))
                    {
                        using (var app = apps.OpenSubKey(name))
                        {
                            if (app == null || Convert.ToString(app.GetValue("SystemComponent", "0")) == "1") continue;
                            var title = Product.Clean(app.GetValue("DisplayName") as string, 180); if (title.Length == 0) continue;
                            var publisher = Product.Clean(app.GetValue("Publisher") as string, 120);
                            if (report.Apps.Count >= 1024) break;
                            if (report.Apps.Any(a => a.Name == title && a.Publisher == publisher)) continue;
                            report.Apps.Add(new InstalledApp { Name = title, Publisher = publisher, Version = Product.Clean(app.GetValue("DisplayVersion") as string, 80),
                                Note = Policy.IsProtectedText(title + " " + publisher) ? "Security / hardware: keep unless you know otherwise" : "Review manually in Programs and Features" });
                        }
                    }
            }
            catch (Exception ex) { Note(report, "App inventory incomplete", ex); }
        }
    }
}
