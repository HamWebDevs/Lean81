using System;
using System.ComponentModel;
using System.Linq;
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
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateService(IntPtr manager, string name, string display, uint access, uint type, uint start, uint error, string binary, string group, IntPtr tag, IntPtr dependencies, string account, string password);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool DeleteService(IntPtr service);
    [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr service);
    static bool FixtureServiceName(string name) { return name != null && Regex.IsMatch(name, "^Lean81Fixture[0-9a-f]{32}$"); }
    static void ServiceBaseRun(string name) { using (var service = new IdleFixtureService(name)) ServiceBase.Run(service); }
    sealed class DisposableFixtureService : IDisposable
    {
        public readonly string Name = "Lean81Fixture" + Guid.NewGuid().ToString("N");
        public readonly ServiceController Controller; readonly IntPtr handle;
        public DisposableFixtureService(DisposableFixtureService provider)
        {
            if (Environment.GetEnvironmentVariable("LEAN81_NATIVE_FIXTURES") != "ephemeral-ci") throw new InvalidOperationException("Ephemeral CI only.");
            IntPtr manager = IntPtr.Zero, dependencies = IntPtr.Zero;
            try
            {
                manager = OpenSCManager(null, null, 1 | 2); if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (provider != null) dependencies = Marshal.StringToHGlobalUni(provider.Name + "\0");
                string binary = "\"" + typeof(Tests).Assembly.Location + "\" --idle-service " + Name;
                handle = CreateService(manager, Name, Name, 0xF01FF, 16, 3, 1, binary, null, IntPtr.Zero, dependencies, null, null);
                if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                Controller = new ServiceController(Name);
            }
            catch { if (handle != IntPtr.Zero) { try { DeleteService(handle); } finally { CloseServiceHandle(handle); } } throw; }
            finally { if (dependencies != IntPtr.Zero) Marshal.FreeHGlobal(dependencies); if (manager != IntPtr.Zero) CloseServiceHandle(manager); }
        }
        public void Dispose()
        {
            try { Controller.Refresh(); if (Controller.Status != ServiceControllerStatus.Stopped) { Controller.Stop(); Controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); } }
            finally
            {
                try { if (!DeleteService(handle)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
                finally { CloseServiceHandle(handle); Controller.Dispose(); }
            }
        }
    }
    static void NativeServiceFixtures()
    {
        using (var provider = new DisposableFixtureService(null))
        using (var child = new DisposableFixtureService(provider))
        {
            child.Controller.Start(); child.Controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            var parentState = NativeServices.Read(provider.Name); parentState.StartType = 4; parentState.Enabled = false;
            var childState = NativeServices.Read(child.Name); childState.StartType = 4; childState.Enabled = false;
            // Only these freshly created names reach the native primitive directly.
            // Production allowlists are never relaxed for test fixtures.
            NativeServices.Set(parentState, true); NativeServices.Set(childState, true);
            Test("native disabled-running dependents remain protected", () => Assert(NativeServices.Read(provider.Name).Dependents.Contains(child.Name)));
            NativeServices.Set(childState, false); NativeServices.Set(parentState, false);
            Test("native failed start restores the original disabled start type", () => {
                Throws(() => NativeServices.Set(childState, true)); var now = NativeServices.Read(child.Name); Assert(now.StartType == 4 && !now.Running && !now.Enabled);
            });
            Test("native disabled-running fixture group restores provider then dependent exactly", () => {
                NativeServices.Set(parentState, true); NativeServices.Set(childState, true);
                Assert(NativeServices.Read(provider.Name).Fingerprint() == parentState.Fingerprint() && NativeServices.Read(child.Name).Fingerprint() == childState.Fingerprint());
            });
        }
    }
}
