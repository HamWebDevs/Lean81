using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32.SafeHandles;
using System.Linq;

namespace Lean81
{
    sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() { return NativeServices.CloseServiceHandle(handle); }
    }
    static class NativeServices
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Config { public uint Type, Start, Error; public IntPtr Binary, Group; public uint Tag; public IntPtr Dependencies, Account, Display; }
        [StructLayout(LayoutKind.Sequential)]
        struct ServiceStatus { public uint Type, State, Accepted, Exit, SpecificExit, Checkpoint, WaitHint; }
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool ControlService(ServiceHandle service, uint control, out ServiceStatus status);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern ServiceHandle OpenSCManager(string machine, string database, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool CloseServiceHandle(IntPtr handle);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryServiceConfig(ServiceHandle service, IntPtr config, uint size, out uint needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryServiceConfig2(ServiceHandle service, uint level, IntPtr config, uint size, out uint needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ChangeServiceConfig(ServiceHandle service, uint type, uint start, uint error, string binary, string group, IntPtr tag, string dependencies, string account, string password, string display);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ChangeServiceConfig2(ServiceHandle service, uint level, ref int delayed);
        static void Check(bool result) { if (!result) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        static ServiceHandle Open(ServiceHandle scm, string name, uint access)
        {
            var result = OpenService(scm, name, access);
            if (result.IsInvalid) { result.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); } return result;
        }
        static string Str(IntPtr value) { return Marshal.PtrToStringUni(value) ?? ""; }
        static string Multi(IntPtr value)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (int n = 0; value != IntPtr.Zero && n < 256; n++)
            {
                string text = Str(value); if (text.Length == 0) break;
                parts.Add(text); value = IntPtr.Add(value, (text.Length + 1) * 2);
            }
            return String.Join("|", parts);
        }
        public static ItemState Read(string name)
        {
            using (var scm = OpenSCManager(null, null, 1))
            using (var service = Open(scm, name, 1 | 4 | 8))
            using (var controller = new ServiceController(name))
            {
                uint needed; QueryServiceConfig(service, IntPtr.Zero, 0, out needed);
                if (needed == 0 || needed > 65536) throw new InvalidOperationException("Unsupported service configuration size.");
                var memory = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Check(QueryServiceConfig(service, memory, needed, out needed));
                    var c = (Config)Marshal.PtrToStructure(memory, typeof(Config));
                    var state = new ItemState { Kind = "service", Target = name, Scope = "machine", Exists = true,
                        StartType = (int)c.Start, Enabled = c.Start != 4,
                        Running = controller.Status == ServiceControllerStatus.Running,
                        Definition = Product.Hash(c.Type + "\n" + c.Error + "\n" + Str(c.Binary) + "\n" + Str(c.Group) + "\n" + Multi(c.Dependencies) + "\n" + Str(c.Account) + "\n" + Str(c.Display)) };
                    if (controller.Status != ServiceControllerStatus.Running && controller.Status != ServiceControllerStatus.Stopped)
                        throw new InvalidOperationException("Service is paused or changing state; rescan later.");
                    uint ignored; var delay = Marshal.AllocHGlobal(4);
                    try { Check(QueryServiceConfig2(service, 3, delay, 4, out ignored)); state.Delayed = Marshal.ReadInt32(delay) != 0; }
                    finally { Marshal.FreeHGlobal(delay); }
                    var dependents = controller.DependentServices;
                    try
                    {
                        state.Dependents = dependents.Where(d => ReadStart(scm, d.ServiceName) != 4 || d.Status != ServiceControllerStatus.Stopped).Select(d => d.ServiceName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                    }
                    finally { foreach (var d in dependents) d.Dispose(); }
                    return state;
                }
                finally { Marshal.FreeHGlobal(memory); }
            }
        }
        static uint ReadStart(ServiceHandle scm, string name)
        {
            using (var service = Open(scm, name, 1))
            {
                uint size; QueryServiceConfig(service, IntPtr.Zero, 0, out size);
                if (size == 0 || size > 65536) throw new InvalidOperationException("Cannot inspect dependent service.");
                var p = Marshal.AllocHGlobal((int)size);
                try { Check(QueryServiceConfig(service, p, size, out size)); return ((Config)Marshal.PtrToStructure(p, typeof(Config))).Start; }
                finally { Marshal.FreeHGlobal(p); }
            }
        }
        public static void CheckAccess(ItemState state, bool restoring)
        {
            using (var scm = OpenSCManager(null, null, 1))
            using (var service = Open(scm, state.Target, 1 | 2 | 4 | 8 | (restoring && state.Running ? 16u : !restoring && state.Running ? 32u : 0u))) { }
        }
        public static void Set(ItemState original, bool restore)
        {
            using (var scm = OpenSCManager(null, null, 1))
            using (var service = Open(scm, original.Target, 1 | 2 | 4 | (restore ? 0u : 32u)))
            using (var controller = new ServiceController(original.Target))
            {
                if (!restore && original.Delayed) { int no = 0; Check(ChangeServiceConfig2(service, 3, ref no)); }
                // A disabled-but-running service needs a temporary demand start to
                // restore its running state. Always put its disabled start type back.
                bool restartDisabled = restore && original.StartType == 4 && original.Running;
                Check(ChangeServiceConfig(service, UInt32.MaxValue, restartDisabled ? 3 : restore ? (uint)original.StartType : 4, UInt32.MaxValue, null, null, IntPtr.Zero, null, null, null, null));
                try
                {
                if (restore && original.StartType == 2) { int delayed = original.Delayed ? 1 : 0; Check(ChangeServiceConfig2(service, 3, ref delayed)); }
                controller.Refresh();
                if (!restore && controller.Status == ServiceControllerStatus.Running)
                {
                    // ControlService affects this one service; ServiceController.Stop() would recurse.
                    ServiceStatus status; Check(ControlService(service, 1, out status));
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                if (restore && original.Running && controller.Status == ServiceControllerStatus.Stopped)
                { controller.Start(); controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15)); }
                }
                finally
                {
                    if (restartDisabled) Check(ChangeServiceConfig(service, UInt32.MaxValue, 4, UInt32.MaxValue, null, null, IntPtr.Zero, null, null, null, null));
                }
            }
        }
    }
}
