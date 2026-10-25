# Working on Lean81

- Build a C# 5 / .NET Framework 4.5.1 Windows Forms app; Windows 8.1 is the target. No Python, Electron, runtime downloads, telemetry or resident service.
- Compile against the pinned Microsoft 4.5.1 reference assemblies. Keep actual Windows 8.1 validation separate from modern Windows CI.
- Never run startup commands, task actions, uninstall strings or downloaded code during inspection.
- System-setting mutations must go through the allowlisted backend and journal engine. Preserve exact state, fresh-state preflight, dependency checks, admin-only machine journals and conflict-aware recovery.
- Protect networking, security, input and core OS services. Optional services require an explicit unused-feature choice. Microsoft/system scheduled tasks are inspection-only.
- No registry cleaners, RAM cleaners, pagefile/timer/kernel tweaks, driver removal, blanket process killing or global security/update changes. Reviewed app closure must use the fixed companion catalog, same user/session, critical/foreground protection and fresh creation-time identity on an open process handle; never auto-relaunch inspected commands.
- Keep all tests isolated. Native mutation tests must use uniquely named disposable fixtures on ephemeral CI and clean up in finally blocks; never toggle a real installed service or existing task.
- Run core transaction/policy tests, Windows native fixture tests, and actual GUI smoke/screenshots before releasing. Inspect rendered GUI screenshots.
- Keep version, executable metadata, documentation and release files aligned. Use HamWebDevs for public owner credits.
