using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lean81
{
    public static class PowerPlans
    {
        public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access, uint index, out Guid buffer, ref uint size);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
        static bool Exists(Guid wanted)
        {
            for (uint i = 0; i < 128; i++)
            {
                uint size = 16; Guid current; uint error = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, out current, ref size);
                if (error == 259) return false;
                if (error != 0) throw new Win32Exception((int)error);
                if (current == wanted) return true;
            }
            return false;
        }
        public static ItemState Read()
        {
            IntPtr pointer; uint error = PowerGetActiveScheme(IntPtr.Zero, out pointer);
            if (error != 0) throw new Win32Exception((int)error);
            string active;
            try { active = ((Guid)Marshal.PtrToStructure(pointer, typeof(Guid))).ToString("D"); } finally { LocalFree(pointer); }
            bool available = Exists(new Guid(HighPerformance));
            return new ItemState { Kind = "power", Scope = "user", Target = "active-power-plan", Exists = available, Enabled = available && active != HighPerformance, Value = active };
        }
        public static void Validate(ItemState state)
        {
            Guid guid;
            if (state.Target != "active-power-plan" || state.Scope != "user" || !state.Exists || !Guid.TryParseExact(state.Value, "D", out guid) || state.Enabled != (state.Value != HighPerformance))
                throw new InvalidOperationException("Unsupported power-plan operation.");
        }
        public static void Check(ItemState original, bool restoring)
        {
            Validate(original);
            if (!Exists(new Guid(restoring ? original.Value : HighPerformance))) throw new InvalidOperationException("The selected saved power plan is no longer installed.");
            if (!restoring && SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online) throw new InvalidOperationException("Plug the laptop into mains power before applying the performance plan, or leave that option unchecked.");
        }
        public static void Set(ItemState original, bool restoring)
        {
            Check(original, restoring); var guid = new Guid(restoring ? original.Value : HighPerformance);
            uint error = PowerSetActiveScheme(IntPtr.Zero, ref guid); if (error != 0) throw new Win32Exception((int)error);
        }
    }
}
