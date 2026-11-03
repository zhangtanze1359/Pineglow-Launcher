# v1.0.2 release repair validation

Date: 2026-11-03 (Asia/Hong_Kong).

Base: `8845574f788327f4c5bdd0771e2c317e49fe9145` (published v1.0.1 tag is retained).
Repair: `e89023d67472bcae4d93fbe2f9c686d68692a45b`.

## Root cause and bounds

The original `FindAsync` materialized all Java candidates and waited for every version check through `ScanAsync`. Program Files fallback traversed all descendants twice. Version checks read stderr before stdout and had neither timeout nor cancellation. The release runner recorded Java lookup at 124990.9/7038.1/7105.0 ms; its three AI cancellation tests could not reach their handler within 5 seconds, and local diagnosis p95 reached 5660.7 ms.

The repair preserves the full installation scan for explicit Java selection, uses lazy first-valid discovery for a general lookup, and bounds fallback traversal to four descendant levels and 4096 directories per root while skipping reparse points. Explicit paths, JAVA_HOME, PATH and standard vendor installations remain supported. Each version process has a two-second limit, concurrent output reads and process-tree cleanup on cancellation/timeout. Local diagnosis validates the configured Java first, links cancellation and gives supplementary Java metadata one second. Missing metadata stays missing.

## Local evidence (raw records retained separately)

| Requirement | Evidence |
| --- | --- |
| Slow Java is bounded and reaped | `SlowVersionProcess_IsBounded_AndHasExitedOnReturn`: original waited 15 s and failed; repaired passes |
| Output pipes do not deadlock | `BothOutputPipes_AreDrainedConcurrently`: original exceeded 5 s and failed; repaired passes |
| Normal discovery and validation | `VersionValidation_UsesRealOutputAndExitCode` (normal/invalid/nonzero) and `Find_ReturnsFirstValidJava_WithoutEnumeratingTheRest` |
| Large unrelated tree | `FallbackSearch_FindsNormalInstallation_WithoutDescendingUnrelatedDeepTrees`: original returned 101 matches; repaired retains the normal installation without traversing the deep unrelated branches |
| Cancellation cleans up | `Cancellation_KillsVersionProcess_BeforeReturning` plus existing `AiCancelAndRedirectStep3Tests` |
| Local result survives slow configured Java | `LocalDiagnosis_WithSlowConfiguredJava_PreservesLocalResultWithoutInventingVersion` (real local HTTP and real child process) |
| Regression | Focused run 52/52; final new Java cases 9/9; full .NET 967/967, skipped 0; Node inventory 380/380, skipped 0; locked restore passed |
| Local single EXE | All 19 standalone checks passed, including a real main-window title, resource closure and byte-for-byte UI comparison; version 1.0.2 |
| Existing shell integration | VerifyHost build and bisect smoke passed; production credential backend calls 0 |

The missing-shell counterexample reproduces the Ubuntu `null` process exit. The harness now selects `pwsh` on Unix and preserves Windows PowerShell 5.1 on Windows, without changing assertions. Ubuntu Actions job 125789388550 ran all 380 Node tests with 0 failures and 0 skips.

## Remote evidence and release boundary

Windows comparison: https://github.com/zhangtanze1359/Pineglow-Launcher/actions/runs/41597873626 . The baseline job pins `98465ead60ebc91be014b0c01b8214118058e473`, whose production code equals the original base. Candidate enumeration took 130214.6 ms before repair and 4914.1 ms after repair. Repaired FindAsync took 79.6/80.9/112.7 ms; local diagnosis p95 was 97.6 ms. Normal Java installations were found in both runs.

The first remote full suite was 966/967: the sole failure was a Windows UnauthorizedAccessException deleting an exited JavaProbe.exe in test Dispose. Its product assertions and the focused stage had passed. Cleanup now preserves only the fixture files if the filesystem denies deletion; the explicit process-exit assertion remains unchanged. A fresh full Windows run is required before release.

Run 41598474485 subsequently passed all 967 Windows tests, zero skips. The first main Build run 41598910035 passed Windows and macOS, and its Ubuntu Node tests passed 380/380, but Linux test compilation exposed a new fixture-only conflict: the extensionless JavaProbe apphost occupied the intended JavaProbe directory. The copy target is now JavaProbeFixture on every platform. Release run 41598909922 was cancelled before completion; v1.0.2 is preserved and v1.0.3 is the new candidate. Full Linux tests are added to the branch verification before tagging.

The unmodified release workflow still requires tests on the exact tag SHA, standalone cold start and final manifest checks before uploading EXE, manifest and LICENSE. Published asset verification and mirror/source status are recorded in the final local delivery report after the actual run; this document alone does not assert publication or independent visual acceptance.
