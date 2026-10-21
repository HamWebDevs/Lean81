# Compatibility

Lean81 is C# 5 targeting **.NET Framework 4.5.1** and Windows 8.1. Windows 8.1 includes that framework. The portable executable is AnyCPU and can run as a 32-bit or 64-bit process; it inspects the corresponding machine Run registry views explicitly. No Python or browser engine is used.

Builds use Microsoft's `Microsoft.NETFramework.ReferenceAssemblies.net451` version 1.0.3, checked against a pinned SHA256. These are build-time files and are not shipped in the portable package. Compiling against these references prevents accidental dependencies on newer managed APIs; it does not prove behavior on an old Windows installation.

| Platform | Status |
| --- | --- |
| Windows 8.1 x86 / x64 | Design and API target; physical-device validation pending |
| Windows Server 2022, GitHub-hosted runner | Automated compile, transaction tests, disposable Run/task fixtures, native capture and GUI rendering |
| Windows 10 / 11 | Expected with .NET Framework 4.5.1 or later installed; desktop validation pending |
| Linux / macOS | Not supported |

Windows 8.1 is no longer supported by Microsoft. Lean81 does not make the operating system secure or current, and it does not disable security or update components.

## Limits

- Startup changes cover string values in HKCU Run and HKLM Run (32/64-bit views). RunOnce, startup-folder shortcuts and StartupApproved metadata are not modified. A registered Run entry may already be blocked by Windows Task Manager.
- Scheduled tasks are bounded to 512 inventory items in total and 12 folder levels. Hidden tasks are included where accessible; Microsoft/system, recognized security and hardware tasks are inspection-only. The task's author, path and XML content inform conservative protection heuristics; these are not a software trust/signature verifier.
- Optional services are a fixed allowlist. A service with a non-disabled dependent is kept. Some devices have OEM dependencies not visible to Windows service dependency checks; review each tradeoff and test features after a change.
- App inventory reads traditional uninstall registry entries. Store apps and per-app background permissions are outside this release's scope. Use Programs and Features to uninstall apps yourself.
- CPU capture observes two-second intervals, normalizes process CPU across all logical processors, and reports up to five accessible busy processes per sample. Inaccessible, newly started or short-lived processes may be missed. Observing CPU uses a small amount of CPU itself; the trace records observer CPU time. No wake-delay probe is present.
- No improvement percentage is promised. A background item can be idle, and disabling prefetch may slow launches. Compare the same workload with the same power state.
- The executable is unsigned. There is no installer, auto-update component, telemetry, scheduled Lean81 job or resident service.

Framework reference: https://learn.microsoft.com/en-us/dotnet/framework/deployment/deployment-guide-for-developers

Windows lifecycle: https://learn.microsoft.com/en-us/lifecycle/products/windows-81
