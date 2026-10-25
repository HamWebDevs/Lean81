# Compatibility

Lean81 is C# 5 targeting **.NET Framework 4.5.1** and Windows 8.1. Windows 8.1 includes that framework. The portable executable is AnyCPU and can run as a 32-bit or 64-bit process; it inspects the corresponding machine Run registry views explicitly. No Python or browser engine is used.

Builds use Microsoft's `Microsoft.NETFramework.ReferenceAssemblies.net451` version 1.0.3, checked against a pinned SHA256. These are build-time files and are not shipped in the portable package. Compiling against these references prevents accidental dependencies on newer managed APIs; it does not prove behavior on an old Windows installation.

| Platform | Status |
| --- | --- |
| Windows 8.1 x86 / x64 | Design and API target; physical-device validation pending |
| Windows Server 2022, GitHub-hosted runner | Automated compile, transaction tests, disposable Run/task fixtures, native capture/device snapshots and GUI rendering at two sizes |
| Windows 10 / 11 | Expected with .NET Framework 4.5.1 or later installed; desktop validation pending |
| Linux / macOS | Not supported |

Windows 8.1 is no longer supported by Microsoft. Lean81 does not make the operating system secure or current, and it does not disable security or update components.

## Limits

- Startup changes cover string values in HKCU Run and HKLM Run (32/64-bit views). RunOnce, startup-folder shortcuts and StartupApproved metadata are not modified. A registered Run entry may already be blocked by Windows Task Manager.
- Scheduled tasks are bounded to 512 inventory items in total and 12 folder levels. Hidden tasks are included where accessible; Microsoft/system, recognized security and hardware tasks are inspection-only. The task's author, path and XML content inform conservative protection heuristics; these are not a software trust/signature verifier.
- Optional services are a fixed allowlist. A service with a non-disabled dependent is kept. Some devices have OEM dependencies not visible to Windows service dependency checks; review each tradeoff and test features after a change.
- App inventory reads traditional uninstall registry entries. Store apps and per-app background permissions are outside this release's scope. Use Programs and Features to uninstall apps yourself.
- CPU capture observes two-second intervals, normalizes process CPU across all logical processors, and reports up to 20 accessible CPU, working-set, private-memory and I/O leaders per sample. Inaccessible, newly started or short-lived processes may be missed. Observing CPU uses a small amount of CPU itself; the trace records observer CPU time. No wake-delay probe is present.
- No improvement percentage is promised. A background item can be idle, and disabling prefetch may slow launches. Compare the same workload with the same power state.
- The executable is unsigned. There is no installer, auto-update component, telemetry, scheduled Lean81 job or resident service.

Framework reference: https://learn.microsoft.com/en-us/dotnet/framework/deployment/deployment-guide-for-developers

Windows lifecycle: https://learn.microsoft.com/en-us/lifecycle/products/windows-81

## Version 0.2 additions

Quick setup and presets only fill usage answers. They do not apply actions. Workspace JSON is bounded to 128 KiB and 12 aggregate results. Invalid settings fall back to keeping every feature, with a warning. Preferences contain no change plans and are never interpreted as executable commands.

Comparisons use a time-weighted CPU mean and require distinct results, matching machine/hardware, synthetic status, workload label and known power source. Both captures must be complete, have CPU coverage for every sample, last at least 15 observed seconds and have durations within a factor of 1.25. Matching labels are not proof of matching workloads or causality.

Fixed-drive capacity is read through DriveInfo; it is not a SMART, SSD, temperature or disk-health test. Uptime comes from GetTickCount64 and reflects Windows boot uptime, which can include a Fast Startup session. Windows tools may expose options outside Lean81's allowlist: the user reviews those tools' own actions.

CSV exports quote strings and neutralize formula-leading process names. HTML reports encode dynamic content, load no remote resources and omit raw command/path/task/receipt fields. Software names are optional; workload labels are excluded from support reports.

Reference APIs: https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount64 and https://learn.microsoft.com/en-us/dotnet/api/system.io.driveinfo.availablefreespace?view=netframework-4.5.1

## Version 1.0 resource and tuning details

Quick tune stages reviewed service choices and, only with a separate opt-in, three fixed desktop DWORDs. New optional-feature answers default to kept when missing from an older profile. Schema-1 receipts and aggregate history remain supported. Desktop effects use existing HKCU Explorer\Advanced or Windows\DWM keys only and save original absence/value; sign out and back in to refresh Explorer. Native test fixtures inspect these values without changing them; exact writes/recovery use in-memory tests.

Disk counters use one short-lived PDH query with language-neutral paths for PhysicalDisk(_Total) idle time, throughput, average queue, and Memory page-ins. Busy time is 100 minus valid idle time. Missing/invalid counters remain unknown and do not fail CPU capture. Aggregate disk activity can hide a single busy drive and does not attribute activity to a process.

GetPerformanceInfo provides commit usage, kernel pools and cache. GlobalMemoryStatusEx provides available physical memory. Kernel CPU is kernel time excluding idle, normalized across all logical processors; it is not CPU attributed exclusively to the System process. GetProcessIoCounters read/write transfer deltas include file, network and device operations. They are not per-process disk utilization. Process handles request query-limited-information access; inaccessible processes are skipped. Memory-heavy zero-CPU leaders are retained and PID/start-time pairs prevent reuse from merging observations.

Disk comparisons require valid disk readings in every sample of both otherwise-compatible captures. Legacy summaries have unknown resource values. Findings use simple review thresholds; they do not diagnose driver leaks, failing disks or guaranteed improvements. Cached memory is reusable, working sets can share pages and page-ins may read mapped files.

API references:

- https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw
- https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcountervalue
- https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-performance_information
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getprocessiocounters

Desktop Peek location reference: https://learn.microsoft.com/en-us/answers/questions/1282989/prevent-window-from-disapearing-when-hovering-anot
