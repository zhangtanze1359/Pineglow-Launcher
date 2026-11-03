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

Windows comparison and full suite: https://github.com/zhangtanze1359/Pineglow-Launcher/actions/runs/41597873626 . The baseline job pins `98465ead60ebc91be014b0c01b8214118058e473`, whose production code equals the original base. Detailed logging retains passing test measurements. Its result must be inspected before release; starting the job alone is not acceptance.

The unmodified release workflow still requires tests on the exact tag SHA, standalone cold start and final manifest checks before uploading EXE, manifest and LICENSE. Published asset verification and mirror/source status are recorded in the final local delivery report after the actual run; this document alone does not assert publication or independent visual acceptance.
