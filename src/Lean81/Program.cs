using System;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Lean81")]
[assembly: System.Reflection.AssemblyDescription("A small, reversible background-load workbench for Windows 8.1 laptops")]
[assembly: System.Reflection.AssemblyCompany("HamWebDevs")]
[assembly: System.Reflection.AssemblyProduct("Lean81")]
[assembly: System.Reflection.AssemblyCopyright("Copyright © 2026 HamWebDevs")]
[assembly: System.Reflection.AssemblyVersion("0.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.5.1", FrameworkDisplayName = ".NET Framework 4.5.1")]

namespace Lean81
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool demo = args.Length == 1 && args[0] == "--demo";
            bool smoke = args.Length == 2 && args[0] == "--gui-smoke";
            bool admin = args.Length == 2 && args[0] == "--admin-user";
            if (args.Length > 0 && !demo && !smoke && !admin) { MessageBox.Show("Supported options: --demo, --gui-smoke <output-directory>. Launch without options for normal use.", "Lean81"); return 2; }
            if (admin && WindowsIdentity.GetCurrent().User.Value != args[1]) { MessageBox.Show("Use the same Windows account for inspection and administrator mode. Alternate-account elevation cannot safely change your sign-in items.", "Lean81"); return 3; }
            try
            {
                IBackend backend = demo || smoke ? (IBackend)new DemoBackend() : new WindowsBackend();
                IJournalStore store = demo || smoke ? (IJournalStore)new MemoryJournals() : new JournalStore(backend.MachineScope);
                using (var form = new MainForm(backend, store, demo || smoke))
                {
                    Timer timer = null; Exception smokeError = null;
                    if (smoke)
                    {
                        string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output); int page = 0, ticks = 0;
                        timer = new Timer { Interval = 350 };
                        timer.Tick += (s, e) =>
                        {
                            try
                            {
                                if (++ticks > 80) throw new InvalidOperationException("GUI scan timed out.");
                                if (!form.SmokeReady) return;
                                if (page < 8) { form.SmokePage(page); form.Refresh(); form.SaveScreenshot(Path.Combine(output, "page-" + page + ".png")); page++; }
                                else if (page == 8) { form.ClientSize = new System.Drawing.Size(980, 620); form.SmokePage(2); form.Refresh(); form.SaveScreenshot(Path.Combine(output, "small-window.png")); page++; }
                                else { timer.Stop(); File.WriteAllText(Path.Combine(output, "gui-smoke.txt"), "PASS: 8 pages and small window rendered in synthetic demo mode."); form.Close(); }
                            }
                            catch (Exception ex) { smokeError = ex; timer.Stop(); form.Close(); }
                        };
                        form.Shown += (s, e) => timer.Start();
                    }
                    Application.Run(form); if (timer != null) timer.Dispose();
                    if (smokeError != null) { File.WriteAllText(Path.Combine(Path.GetFullPath(args[1]), "gui-error.txt"), smokeError.ToString()); return 1; }
                }
                return 0;
            }
            catch (Exception ex) { if (smoke) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "gui-error.txt"), ex.ToString()); } else MessageBox.Show(Product.Clean(ex.Message, 900), "Lean81 could not start"); return 1; }
        }
    }
}
