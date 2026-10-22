# Using Lean81 0.3

## Start with three choices

Open **Quick setup**, keep uncertain features checked, and continue to the optional-service list. A preset fills the answers so you can inspect them. It never changes Windows. Printing/PDF printers, indexed search and application preloading remain checked in every preset because disabling them can make ordinary laptop use worse.

| Preset | Answers it clears |
| --- | --- |
| Keep every feature | None |
| No home or media sharing | HomeGroup, media-player sharing, offline network files |
| No sharing, Bluetooth or touch | The sharing answers above, plus Bluetooth and touch/pen/on-screen keyboard |

The **Why / next step** column explains entries that are kept, protected or already disabled. **Selectable only** reduces the list to supported choices. **Select unused services** selects eligible optional services for review; it does not apply them. Startup entries and tasks always require recognition and individual choice.

**Recommendations** routes you to useful next steps based on the scan, your answers, drive capacity and any CPU observations. A selectable entry is not proof that it wastes CPU. Unknown apps are not automatically labeled bloatware.

## Review and recovery

You can inspect or save a text review without administrator rights. **Enable changes & rescan** asks Windows for administrator permission under the same account. Usage answers carry over through the local preferences file; selections are made again against a fresh scan. No saved or imported apply plan is trusted by the privileged process.

The app saves original settings before its first write and rechecks each selected state. **Recovery → Check saved states** is a read-only assessment of current versus recorded settings. **Restore selected receipt** still performs a fresh full preflight. See [RECOVERY.md](RECOVERY.md) for partial operations, independent edits and manual-review cases.

Disabling a scheduled task prevents future automatic launches. It does not stop an already running instance. Startup changes affect future sign-ins. Service changes disable start type and stop that one service, without recursively stopping dependents.

## CPU capture and saved results

1. Give the activity a short workload label, such as Browsing.
2. Capture 30, 60 or 120 seconds while doing that activity.
3. Open **Saved results & baseline**, select the result and choose **Use as baseline**.
4. Make a deliberate change, let startup settle, then repeat the same activity and duration on the same power source.
5. Select the new result to see the comparison, or click **Compare selected**.

History stores at most 12 aggregate summaries. It retains the baseline when aging out old results. Delete a selected result to remove it locally. Raw process observations remain in memory until the app closes unless you explicitly export a trace. Summaries retain a workload label and numeric results; they contain no process names, PIDs or command paths.

CPU means are weighted by observed sample intervals. The comparison reports absolute percentage-point differences, peak sampled CPU and seconds observed at 80%+ CPU. It checks distinct identities, matching hardware/machine, matching synthetic status and workload labels, complete captures, full CPU sample coverage, a known matching power source, at least 15 observed seconds and similar durations. A failed condition is explained instead of generating a misleading improvement score.

These checks cannot prove identical workloads or causality. Process lists are limited to the five accessible busy processes per two-second sample; inaccessible and short-lived processes can be missed. Capture has an observer cost, recorded in the trace. No wake-delay probe is included, and nothing monitors continuously after the app closes.

## Device, storage and Windows tools

**Device & storage** shows CPU, logical processors, Windows architecture, physical and available memory, Windows boot uptime, power/battery when available, and available capacity on fixed drives. Free space is for the current account. Low-space guidance means under 10% or under 5 GiB available; it is a practical review threshold, not a performance guarantee.

No disk-health, SSD, SMART or temperature diagnosis is inferred from capacity. **Windows Disk Cleanup** opens the normal Windows dialog. Lean81 does not select categories or delete files for you.

**Help & tools** provides fixed shortcuts to Task Manager, Resource Monitor, Task Scheduler, Services, Programs and Features, Disk Cleanup, Power Options, Device Manager and Performance Monitor. Tool launch arguments come from the built-in catalog, never inspected commands or preferences. Demo mode does not launch Windows tools.

## Export and share

- **Export report:** a responsive, printable local HTML report with recommendations, scan/device/capture timestamps, inventory coverage and CPU evidence. From Saved results, use **Export selected** to choose which result is included. Include the retained baseline to apply the same comparison checks used in the app; mismatches show reasons without differences. Aggregate history is a separate opt-in, with up to 12 numbered observations. Workload labels and machine identities are always withheld. Software names are omitted by default. Raw startup commands, command-path fields, task XML, receipts, account details, scan-error text and workload labels are excluded. Review the report before sharing it.
- **Export trace → JSONL:** compatible with ReclaimLab schema 1. CPU, available memory, power and process observations are included. Process names are omitted unless explicitly selected. It includes process IDs and memory sizes as described in the dialog.
- **Export trace → CSV:** numeric sample timeline for a spreadsheet, plus the highest observed process per sample. Names stay opt-in; strings are quoted and formula-leading names are neutralized.
- **Save review:** human-readable names and tradeoffs. It is a document for review, not an executable or importable change plan.

No export is uploaded automatically. Reports load no remote scripts, fonts or images. For storage locations and deletion, see [PRIVACY.md](PRIVACY.md).

## Upgrade from 0.1

Close Lean81, extract the new portable ZIP into a new folder, and open the new EXE. Keep `%ProgramData%\Lean81` for recovery. Existing version 0.1 receipts use the same supported schema. Preferences and summaries are stored under `%LocalAppData%\Lean81\workspace.json`; if saving fails, a notice explains that new choices/results remain in this session. Invalid settings fall back to keeping every feature.

This is an unsigned Windows 8.1 / .NET Framework 4.5.1 preview. Automated checks run on newer Windows. Actual Windows 8.1 hardware validation is pending.
