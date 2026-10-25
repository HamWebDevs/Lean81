Lean81 1.2 candidate brings configuration changes, immediate background-app closure and results into one workflow.

- Deep tune reviews both installed settings and recognized running companions in unused categories. Uncheck individual apps or keep them all. Force close stays an explicit choice.
- Apply settings first, then close selected companions. A settings failure skips closure. Fresh identity and foreground checks protect apps that changed during the review.
- Optional short CPU/RAM/disk checks run before and after, inside Lean81. The result shows observed values, per-action outcomes and sortable remaining process load, with direct restore of the saved configuration group. No export or analysis app is needed.
- An already-disabled optional service that is still running can now be stopped, including dependency ordering and exact restoration of its unusual original state.
- Keep existing recovery data when upgrading. The same Windows account and administrator access are required for configuration changes. Closed apps and unsaved work are not restored by the settings journal.

This is a release candidate. The stable download remains 1.1 until candidate validation is complete. Windows 8.1 / .NET Framework 4.5.1 remains the target; physical Windows 8.1 testing is pending. Modern Windows CI does not establish performance gains or compatibility on the actual laptop. The EXE is unsigned.

Short observations vary with workload and power state. Missing counters stay unavailable. No guaranteed speed increase, registry/RAM cleaner, resident monitor or broader system-service shutdown is included.
