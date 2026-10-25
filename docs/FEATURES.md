# Using Lean81 1.2 candidate

## Deep tune

Open **Deep tune**, choose a preset if useful and check every answer. Checked means you use the feature, or want to keep it. Unticked means Lean81 may offer its installed optional service for disabling. A service with enabled dependents stays protected. A preset never applies settings.

| Preset | Answers it clears |
| --- | --- |
| Keep every feature | None |
| No home or media sharing | HomeGroup, media sharing, offline network files |
| No sharing, Bluetooth or touch | The sharing answers above, plus Bluetooth and touch/pen/on-screen keyboard |
| Browser / cloud gaming laptop | Sharing, touch/pen/on-screen keyboard, scanner/image acquisition, portable-device library synchronization, moved-file link tracking |
| Maximum reduction | The above plus printing, Bluetooth, indexing, Superfetch, error reporting, optional telemetry/diagnostic helpers, TV and recognized companion categories |

The first four presets keep printing/PDF printers, indexed search, Superfetch and error reporting. Maximum reduction changes those answers as described below. The browser preset also keeps Bluetooth. Untick indexed search or Superfetch yourself if you want to test those tradeoffs. Preloading may help application launches; fewer enabled services does not automatically mean a faster laptop.

**Reduce desktop effects** and recognized unused startup/task choices have separate checkboxes. **Review deep tune** refreshes the inventory and stages the actual installed choices. Read the exact tradeoffs, then apply. Nothing applies just by selecting a preset or building a review. Unknown startup apps and tasks require individual selection on their inventory pages.

Without administrator rights you can scan, capture, export and close eligible same-user companions. Setting changes require administrator rights. **Enable changes & rescan** opens Lean81 under the same Windows account. Answers are remembered; build the review again against the fresh elevated scan. No apply plan is imported from a file.

## Combined tuning in 1.2

**Also review closing running companions** connects immediate closure to the same deep-tune review. Only recognized background families in categories you marked unused are suggested. On **Close apps now**, uncheck any process you need, or choose **Keep all apps**. Normal close is the default. Force close is a separate checkbox and can lose app work. **Running apps** remains available for individual closure later.

The reviewed process identities and foreground state are checked again before settings. Missing, changed or foreground apps are kept. Saved settings apply first; a failed settings pass skips app closure. Each app is checked again by the native backend immediately before closing. Cancellation stops observations and remaining app actions, while any current setting pass finishes and keeps its receipts.

The review optionally checks resources for about eight seconds before and eight seconds after tuning, with a two-second settling wait. This adds about 18 seconds and writes no recording file. Turn it off to apply without observations. **Tune result** shows each saved setting and process outcome, plus before/after CPU, available physical RAM, aggregate disk busy and commit. **Remaining load** sorts processes from the after-check by CPU, working-set RAM or read/write I/O; memory-heavy idle apps remain visible. Process I/O includes network/device activity, and shared working-set pages must not be added as total RAM. The result stays in memory for this app session; recovery receipts persist.

Differences require at least three valid intervals, six seconds of data, 80% coverage, complete observations, matching demo/real status and one known unchanged power source. Missing counters stay unavailable. CPU/disk/commit changes are percentage points; available RAM is measured in MiB. These short observations can miss spikes and vary with workload; they do not prove a tune caused an improvement. No working-set sum is presented as freed system RAM.

**Restore this tune** restores the result's saved group after conflict checks. Closed apps are reopened manually. Recovery remains available if a result is lost when Lean81 closes.

An optional service can already have a disabled start type yet still be running. It is now offered for stopping when unused. Running disabled dependents also protect their provider. Restoring this unusual state briefly selects Manual to start the service, then restores Disabled in a finally block. Unsupported delayed-start combinations stay for manual review.

## Active tuning in 1.1

Lean81 starts on **Deep tune**. **Maximum reduction** fills unused-feature answers for a browser/cloud-gaming laptop. Check anything you need before reviewing. It disables indexing/preloading in the proposed answers and assumes no printing, Bluetooth, scanning, TV, sync, chat, music or local game launchers. Compatibility assistance and visual themes stay kept. Disabling preloading can slow application launches; app updaters you turn off need manual attention.

The review may include recognized unused startup entries and third-party tasks, plus optional effects and the separately selected power plan. Unknown startup software still requires individual selection. Service providers are included only if all their enabled dependents are also included. The engine applies leaf services first, refreshes state between passes and records batches in one recovery group. A partial failure leaves earlier successful changes recorded, not silently undone.

**Running apps** is an action page. Select recognized companions to close now. A normal close request is the default; **Force close** may be needed for tray/helper processes. Closing can interrupt sync, downloads, music, calls or unsaved app state. The catalog covers named OneDrive, Dropbox, Steam, Epic, GOG Galaxy, Discord, Slack, Teams, Skype, Spotify and Adobe Creative Cloud executables in matching vendor locations. This is recognition, not a signature/trust verifier. Windows paths, browsers, critical processes, other users/sessions and foreground app families are excluded. PID, creation time, owner, session and executable identity are rechecked on an open handle before an action. Apps are not automatically relaunched.

Startup-folder controls cover regular `.lnk`/`.url` files up to 16 KiB. Targets are never executed or resolved during inspection. Redirected paths, protected content/attributes, unsupported ownership and cross-volume moves are kept for manual review. Same-volume original files move to private backups. Recovery checks bytes and file metadata, restores original ownership/permission rules at the original location and refuses a new file occupying that name. Windows may refresh automatic inheritance bookkeeping flags while retaining the same permission rules.

**Recovery → Restore tune group** restores an entire tune in reverse batch order, including providers before dependents. It checks pending receipts for conflicts before group writes. Previously restored receipts are skipped. Closed apps and app work are not restored by configuration recovery.

The **High performance** plan is opt-in. It selects an existing scheme after a fresh mains-power check, saves the previous scheme and never rewrites plan internals. It can use more heat/energy and remains active until changed or restored; no resident watcher changes it on unplug.

No capture or external analysis app is required for any of these actions.

## Background choices and desktop effects

**Startup apps** covers supported HKCU/HKLM Run values and bounded user/common Startup-folder shortcuts. Changes affect future sign-ins; already running apps are not killed. **Scheduled tasks** disables future launches, without stopping a running instance. System and recognized security/hardware tasks are inspection only.

**Optional services** offers 26 allowlisted services across printing, Bluetooth, indexed search, touch input, HomeGroup, media sharing, offline files, Superfetch, scanner/image acquisition, portable-device synchronization, error reporting and file-link tracking. Changing a service disables its start type and stops that service only; dependents are not stopped recursively. Absent services are skipped.

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

Close the old app and extract the new ZIP into a new folder. Keep `%ProgramData%\Lean81` for original settings and `%LocalAppData%\Lean81` for preferences/history. Receipts and workspaces from 0.1–1.0 remain supported. New feature answers default to kept when absent from older preferences.

The EXE is unsigned and targets Windows 8.1 / .NET Framework 4.5.1. Automated checks use newer Windows; physical Windows 8.1 validation is pending. See [COMPATIBILITY.md](COMPATIBILITY.md) and [PRIVACY.md](PRIVACY.md).
