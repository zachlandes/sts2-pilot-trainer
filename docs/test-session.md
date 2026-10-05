# Test sessions

`./scripts/build.sh && ./scripts/fetch-baselib-parity.sh && ./scripts/test-session.sh` prepares the inputs and runs the default suite.
Read the script's final verdict, not dotnet's per-project totals.
An aborted session is not a pass.

The default session runs one test assembly at a time and allows twelve concurrent xUnit collections within it.
That default matches the twelve performance cores on the M4 Max used for local validation.
The Mod tests remain serial because the engine has process-wide state.
Set `RUNMOBILE_TEST_THREADS=6` to use a different positive collection budget.
The number limits collections, not every process in a command's tree: a replay command may keep an idle parent beside its engine child.
Bare `dotnet test` also reads the twelve-collection default from `.runsettings`, but the scripted default additionally serializes MSBuild's test projects.

## Generated coverage selection

The default session excludes only `GeneratedCoverageTests` when every changed path is in this allowlist:

- `docs/` and `demo/`
- `README.md`, `AGENTS.md`, `CLAUDE.md`, `LICENSE`, `.editorconfig`
- `src/Sts2PilotTrainer.Trainer/` and `tests/Sts2PilotTrainer.Trainer.Tests/`

The Trainer project is presentation and sharing policy, not the recorder or the engine replayed by the generated walks.
All other paths run the complete suite, including recorder, replay, engine, coverage, corpus, scripts, build configuration, and unknown new paths.
This is a conservative exclusion rule, not a dependency graph or a row-by-row coverage estimate.
Every generated row runs on any relevant change; no individual row is selected by guessing which content it exercises.

The change set is the diff from `git merge-base HEAD refs/remotes/origin/main` to the working tree, plus untracked non-ignored files.
Renames are read as a deletion and an addition so moving a relevant file into `docs/` cannot hide it.
The script prints the selected scope, comparison commit, and reason before testing.
Missing history, an unreadable change set, or no changed paths means the full suite.
The script does not fetch or change Git refs.

Run `./scripts/test-session.sh --full` to force every generated row.
An explicit invocation such as `./scripts/test-session.sh tests/Sts2PilotTrainer.Mod.Tests/Sts2PilotTrainer.Mod.Tests.csproj -c Release` is also never automatically filtered.
Explicit dotnet arguments retain their own scope and scheduling choices.
Use `./scripts/test-session.sh --full` for release proof and comparisons of complete-suite duration.

## Timeout measurement

The bound is four hours per test assembly and remains in force for both scripted and direct sessions.
On 2026-10-04, an isolated, prepared full session aborted at forty-five minutes with no assertion failure reported.
The Arbiter project completed in 5m07s; Mod was incomplete.
A two-hour measurement override completed Arbiter in 5m09s and still aborted Mod, which was continuing to complete distinct second-act rows near the bound.
That run's TRX session timestamps put Replay at 3.03 seconds and Trainer at 12.92 seconds.
Mod's session lasted 2h00m00s and was incomplete; its printed partial total is not a completed-suite measurement.
That run also reported ten generated-coverage failures: nine recordings missing an opening `ConfirmCardScreen`, and the Lead Paperweight row missing its prompt seam.
Those failures were handed to coverage triage, not fixed or excluded by this change.

A separate Mod-only run under `caffeinate -di`, with a four-hour outer bound and `--blame-hang-timeout 10m --blame-hang-dump-type mini`, completed all 1008 cases in 9204.94 seconds of measured command wall time.
The script's verdict was `TEST SESSION PASSED`; that run reported no failure, no ten-minute hung test, and no dump.
Its slowest completed case was `HeadlessGameplayCaptureTests.ASecondWonRunRecordedInTheSameProcessIsTheSameRecording`, at 146.56 seconds.
The earlier failures being absent in this run does not establish that they were fixed.

Four hours leaves approximately 86m35s, or 56 percent, above the completed Mod command's wall time.
The bound applies to each test assembly, not the sum of the assemblies now run sequentially.
Increasing it does not remove the separate obligation to investigate a genuine hang.
Use blame-hang's test-case receipts for that diagnosis: a stack naming one theory method repeatedly cannot distinguish its different rows.

The first capped full-session attempt was terminated by SIGTERM after approximately 1h52m, before the four-hour per-assembly bound.
Its transcript reported `TEST SESSION FAILED - its transcript could not be recorded`, and the sampler never wrote a completed result.
The available logs do not identify the sender of the signal.
That attempt provides no completed duration or CPU comparison for the cap.
A detached rerun uses a separate process session, `nohup`, `caffeinate -di`, and an EXIT trap with HUP, INT and TERM handlers to record its exit independently of the agent's tool call.
No shell trap can report SIGKILL or a machine shutdown.

The twelve-collection cap limits concurrent collections rather than making Mod's serial generated walks faster.
Serializing assemblies prevents Arbiter's replay subprocesses from competing with those walks in the same scripted session.
The prior Arbiter duration is minutes and the completed Mod duration is hours, so selecting generated coverage by independent paths avoids the dominant work without changing which generated rows run on an engine, replay or recorder change.
The cap's effect on CPU consumption and the completed capped full-session duration have not yet been measured.
