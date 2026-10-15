# R3 baseline history

- r3\g0  : first R3 baseline + GUID execution copy C:\Users\Administrator\AppData\Local\Temp\dsr-rework-r3-905e0b60
            SUPERSEDED. After it was captured, scripts\run-goal-validation.ps1 was fixed further
            (bare command names must stay on PATH; the child exit code must survive the wait).
            The runner itself caught this: re-running against the stale copy refused with
            INPUT_SIZE_MISMATCH even though the manifest file was unchanged, which is exactly the
            LQ-02 behaviour this rework adds. Nothing was rewritten in place; a new baseline and a
            new GUID copy were created instead.
- r3\g1  : final R3 baseline + GUID execution copy (see g1\execution-root.json)