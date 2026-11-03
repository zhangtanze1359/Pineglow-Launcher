# Java discovery release repair implementation plan

Goal: remove the Java discovery bottleneck proven in release v1.0.1 without weakening any existing gate, then publish a new tag on the validated source.

Spec: D:/松光启动器/docs/release-diagnosis-2026-11-03/发布阻断修复流程.md and 执行提示词.md.

SourceRoot: D:/松光启动器 (read only, HEAD 47e939c).
ExecutionRoot: D:/pineglow-release-fix-20261103 (clean base 8845574f788327f4c5bdd0771e2c317e49fe9145).
EvidenceRoot: D:/pineglow-release-fix-evidence-20261103.

Constraints: retain all original dirty/untracked assets; no retagging v1.0.1; no increased test threshold, removed assertion, unbounded background task or fabricated Java version. Publish only reviewed code/tests/workflows/version notes. Existing same-SHA release tests, standalone cold start and manifest validation remain required.

- [ ] Run `JavaDiscoveryTimingTests.CandidateEnumeration_AndVersionProcesses_AreMeasuredSeparately` on Windows Actions before repair; collect enumeration and individual process timings without paths.
- [ ] Add focused Java discovery tests: large unrelated directory tree, a slow real process, cancellation/cleanup, normal Java discovery and invalid version handling. Record failing assertions before changing production.
- [ ] Repair `JavaLocator.cs` at the measured cause; propagate bounded discovery/cancellation to `LocalApiService.HandleDiagnoseInstanceAsync` if needed. Keep normal launch discovery and diagnostics truthful.
- [ ] Run focused Java and AI cancellation/diagnosis tests, all Node inventory tests and all .NET tests. Record counts, skips, commands and exit codes.
- [ ] Investigate Ubuntu Node failure separately (`goal-validation.test.cjs` currently defaults to Windows-only powershell.exe). Reproduce the missing executable, retain assertions and validate platform-correct shell selection.
- [ ] Run Windows Actions stage and full suite checks. Review diff and exact file set, then advance remote main by fast forward only if unchanged; create a fresh v1.0.2 tag after prerequisites pass.
- [ ] Verify same-SHA Release workflow, isolated single-EXE cold start, actual Release attachments/download size/SHA256, source archives, LICENSE and release notes. Record optional Gitee/default update-source outcomes accurately.
- [ ] Rehash the original dirty files and compare original status/HEAD; deliver scoped evidence and self-evaluation.

Execution proceeds inline within the user-provided execution prompt; no additional execution choice is needed.
