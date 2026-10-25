# Using Lean81 1.0

## Quick tune

Open **Quick tune**, choose a preset if useful and check every answer. Checked means you use the feature, or want to keep it. Unticked means Lean81 may offer its installed optional service for disabling. A service with enabled dependents stays protected. A preset never applies settings.

| Preset | Answers it clears |
| --- | --- |
| Keep every feature | None |
| No home or media sharing | HomeGroup, media sharing, offline network files |
| No sharing, Bluetooth or touch | The sharing answers above, plus Bluetooth and touch/pen/on-screen keyboard |
| Browser / cloud gaming laptop | Sharing, touch/pen/on-screen keyboard, scanner/image acquisition, portable-device library synchronization, moved-file link tracking |

Every preset keeps printing/PDF printers, indexed search, Superfetch and error reporting. The browser preset also keeps Bluetooth. Untick indexed search or Superfetch yourself if you want to test those tradeoffs. Preloading may help application launches; fewer enabled services does not automatically mean a faster laptop.

**Also include the three desktop effects** is a separate opt-in. **Build my review** refreshes the inventory and stages eligible services plus those effects. Read the exact tradeoffs, then apply. Nothing applies just by selecting a preset or building a review. Startup apps and scheduled tasks require individual selection.

Without administrator rights you can scan, capture, export and save a text review. **Enable changes & rescan** opens Lean81 under the same Windows account. Answers are remembered; build the review again against the fresh elevated scan. No apply plan is imported from a file.

## Background choices and desktop effects

**Startup apps** covers supported HKCU/HKLM Run values. Changes affect future sign-ins; already running apps are not killed. **Scheduled tasks** disables future launches, without stopping a running instance. System and recognized security/hardware tasks are inspection only.

**Optional services** offers 13 allowlisted services across printing, Bluetooth, indexed search, touch input, HomeGroup, media sharing, offline files, Superfetch, scanner/image acquisition, portable-device synchronization, error reporting and file-link tracking. Changing a service disables its start type and stops that service only; dependents are not stopped recursively. Absent services are skipped.

**Desktop effects** has three fixed Explorer settings: taskbar animations, desktop Peek and icon-label shadows. Selected DWORDs are set to zero. Exact previous values, including absence, are saved. Only existing settings keys are used. Peek uses HKCU\Software\Microsoft\Windows\DWM; the other two use HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced. Sign out and back in to refresh Explorer; Lean81 does not restart Explorer or close your apps. Shadows can affect text readability on your wallpaper.

**Installed apps** filters desktop software by name/publisher and opens the ordinary Windows uninstaller. It never executes registry uninstall commands or labels unknown software as bloatware.

## Resource capture

Record 30, 60 or 120 seconds while the slowdown is happening. Sampling is every two seconds, on demand; no monitor continues after capture ends. **Stop** retains a partial result when samples exist.

The chart uses teal for CPU, blue for physical RAM in use and amber for aggregate physical-disk busy time. Missing observations produce gaps, not zero readings. Open **Memory, disk & processes** for:

- Average available physical RAM, committed memory/commit limit and peak commit pressure.
- Aggregate physical-disk activity, throughput and average queue length.
- Disk page-ins, kernel CPU, paged/nonpaged kernel pools and system cache.
- Process leaders sorted by peak CPU, working-set RAM, private memory or read/write I/O.
- Plain-language findings and a shortcut to Windows Resource Monitor.

Each sample retains the union of its top five CPU, working-set, private-memory and I/O processes, capped at 20. Zero-CPU memory-heavy apps are included. Inaccessible, newly started and short-lived processes may be missed. CPU is normalized across logical processors. Peak process values are observed maxima, not continuous totals. The Samples column shows how often that leader was retained; it is not the process lifetime. Reused PIDs are kept separate during analysis.

Process I/O includes file, network and device operations; it is **not** per-process disk usage. Disk counters are aggregate across physical disks and can hide one busy drive. Use Resource Monitor for detailed attribution. Disk page-ins can read mapped program/data files as well as paging-file data. Reusable cache is not wasted RAM; working sets share pages and must not be added as total physical usage. Kernel-pool size alone does not establish a driver leak.

Missing or disabled counters remain unavailable without preventing the other capture readings. There is no wake-delay, input-latency, SMART, disk-health or temperature probe.

## Baseline and history

Give your activity a label. After capture, open **Saved results & baseline** and choose **Use as baseline**. After a deliberate change, repeat the same activity, duration and power source, then compare a different result.

CPU means are weighted by the observed intervals. Comparisons require distinct results, matching machine/hardware, synthetic state, workload labels and known power, complete captures, full CPU coverage, at least 15 observed seconds and similar durations. Matching labels cannot prove identical workloads or causality. Disk differences additionally require valid disk readings in every sample of both captures; CPU comparisons can remain available when disk data is missing.

Up to 12 aggregate results are retained, including the chosen baseline. New summaries include disk and commit coverage and peak nonpaged pool. Older summaries load with those new fields unavailable. Process names, PIDs and raw samples stay in memory unless you export them; history contains none of those fields.

## Reports, tools and recovery

**Export report** saves responsive, printable offline HTML with next steps, inventory coverage, device capacity and CPU/resource evidence. Saved results can export the chosen result, baseline and optional numbered history. Workload labels, machine identities, raw commands, paths, task XML, private error details and receipts are withheld. Software names are a separate opt-in.

**Export trace** offers ReclaimLab schema-1 JSONL with resource metrics and per-process CPU/memory/I/O, or CSV with the numeric timeline and top CPU process. Process names stay opt-in. CSV strings are quoted and formula-leading names neutralized. No export is uploaded automatically.

**Help & tools** opens fixed Windows tools, including Task Manager, Resource Monitor, Services, Programs and Features and Disk Cleanup. Windows tools have their own actions; Lean81 does not select cleanup categories or delete files.

**Recovery** saves settings before changes, checks original/expected states and restores the exact originals. Independent edits or ambiguous partial states stop automatic recovery. **Check saved states** is read only. See [RECOVERY.md](RECOVERY.md).

## Upgrade

Close the old app and extract the new ZIP into a new folder. Keep `%ProgramData%\Lean81` for original settings and `%LocalAppData%\Lean81` for preferences/history. Receipts and workspaces from 0.1–0.3 remain supported. New feature answers default to kept when absent from older preferences.

The EXE is unsigned and targets Windows 8.1 / .NET Framework 4.5.1. Automated checks use newer Windows; physical Windows 8.1 validation is pending. See [COMPATIBILITY.md](COMPATIBILITY.md) and [PRIVACY.md](PRIVACY.md).
