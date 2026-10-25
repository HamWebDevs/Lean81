using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Lean81;

static partial class Tests
{
    sealed class IdleFixtureService : ServiceBase
    {
        public IdleFixtureService(string name) { ServiceName = name; CanStop = true; AutoLog = false; }
        protected override void OnStart(string[] args) { }
        protected override void OnStop() { }
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr OpenSCManager(string machine, string database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateService(IntPtr manager, string name, string display, uint access, uint type, uint start, uint error, string binary, string group, IntPtr tag, string dependencies, string account, string password);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool DeleteService(IntPtr service);
    [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr service);
    static bool FixtureServiceName(string name) { return name != null && Regex.IsMatch(name, "^Lean81Fixture[0-9a-f]{32}$"); }
    static void ServiceBaseRun(string name) { using (var service = new IdleFixtureService(name)) ServiceBase.Run(service); }
    static void NativeServiceFixtures()
    {
        if (Environment.GetEnvironmentVariable("LEAN81_NATIVE_FIXTURES") != "ephemeral-ci") throw new InvalidOperationException("Ephemeral CI only.");
        string name = "Lean81Fixture" + Guid.NewGuid().ToString("N"); IntPtr manager = IntPtr.Zero, handle = IntPtr.Zero;
        using (var controller = new ServiceController(name))
        {
            try
            {
                manager = OpenSCManager(null, null, 1 | 2); if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                string binary = "\"" + typeof(Tests).Assembly.Location + "\" --idle-service " + name;
                handle = CreateService(manager, name, name, 0xF01FF, 16, 3, 1, binary, null, IntPtr.Zero, null, null, null);
                if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                controller.Start(); controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                var original = NativeServices.Read(name); original.StartType = 4; original.Enabled = false;
                // Only this freshly created fixture name is passed directly to the
                // native primitive. Production allowlists are never relaxed for tests.
                NativeServices.Set(original, true);
                Test("native disabled-running fixture is stopped and restored exactly", () => {
                    var before = NativeServices.Read(name); Assert(before.StartType == 4 && before.Running && !before.Enabled);
                    NativeServices.Set(before, false); var stopped = NativeServices.Read(name); Assert(stopped.Fingerprint() == before.Disabled().Fingerprint());
                    NativeServices.Set(before, true); Assert(NativeServices.Read(name).Fingerprint() == before.Fingerprint());
                });
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    try { controller.Refresh(); if (controller.Status != ServiceControllerStatus.Stopped) { controller.Stop(); controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); } }
                    finally { try { if (!DeleteService(handle)) throw new Win32Exception(Marshal.GetLastWin32Error()); } finally { CloseServiceHandle(handle); } }
                }
                if (manager != IntPtr.Zero) CloseServiceHandle(manager);
            }
        }
    }
}
