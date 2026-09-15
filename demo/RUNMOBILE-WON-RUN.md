# A won run, recorded whole and replayed

*2026-09-15T09:02:36Z by Showboat 0.6.1*
<!-- showboat-id: c58fa05a-bcb2-43e1-8624-931ab049e2bc -->

The game wins a run from the Architect's event room: the last boss's reward screen issues the same act-change vote every act ends with, `EnterNextAct` opens the victory room in place of an act, and the event's PROCEED calls `RunManager.WinRun`, which calls `OnEnded(true)` inside that very press. Under Instant fast mode the two animations before it are skipped, so `OnEnded` runs synchronously before the recorder's pump has polled once, and before this change the PROCEED decision was still pending when the recording was finished: every won recording made that way ended `continuity = broken` with "The run ended with 1 decision(s) the recorder had not finished reading". The replay driver refused the last act's transition besides, so no won recording could pass `gate`.

Headless `Cmd.Wait` is neutralised, so a headless process is that fast-mode setting exactly. The evidence below is the headless proof on this branch: a one-act Ironclad run walked by the whole-act journey's own rules through the real recorder, won at the Architect's PROCEED, validated, and replayed fresh from its seed by `Arbiter.ReplayStartedRun`, the loop `gate` runs past the retail preflight a headless roster rightly fails. No retail client was used; the client slot was not granted for this change, so there is no screenshot of the victory screen with the overlay reading RECORDING COMPLETE.

```bash
cd .. && export HEADLESS_CAPTURE_RECORDINGS="$(mktemp -d)" && dotnet test tests/Sts2PilotTrainer.Mod.Tests -c Release --no-build --filter "FullyQualifiedName~AWonRunFinishesContinuousWithTheDecisionItWasWonOnAndReplays" --logger "console;verbosity=detailed" 2>&1 | grep -E "^\s*(Passed|Failed) |\[Runmobile\] recorded" | sed -E "s#written to .*#written to the kept recording#" && recording="$(ls "$HEADLESS_CAPTURE_RECORDINGS"/*/Runmobile/steam/test/profile1/recordings/*.replay.json)" && echo "--- arbiter validate" && ./scripts/arbiter validate "$recording" 2>&1 | tail -3 && echo "--- the recording ends on the decision the run was won on" && python3 -c "import json,sys; m=json.load(open(sys.argv[1])); print(*[(a[\"seq\"],a[\"verb\"],a[\"args\"]) for a in m[\"actions\"][-3:]], sep=\"\n\"); n=m[\"source\"][\"native\"]; print(\"outcome\", n[\"outcome\"], \"| continuity\", n[\"continuity\"], \"| integrity\", n[\"integrity\"], \"| boundaries\", len(m[\"boundaries\"]))" "$recording"
```

```output
[INFO] [Runmobile] recorded native-A249YBES73-20260915-090401: won, 244 decision(s), 75 boundary/boundaries, continuity continuous, integrity complete, written to the kept recording
  Passed Sts2PilotTrainer.Arbiter.Tests.HeadlessGameplayCaptureTests.AWonRunFinishesContinuousWithTheDecisionItWasWonOnAndReplays [9 s]
--- arbiter validate
integrity: complete
structure: VALID

--- the recording ends on the decision the run was won on
(241, 'ProceedToNextAct', {})
(242, 'ChooseEventOption', {'event_id': 'EVENT.THE_ARCHITECT', 'option_index': '0', 'option_key': 'THE_ARCHITECT.dialogue.0'})
(243, 'ChooseEventOption', {'event_id': 'EVENT.THE_ARCHITECT', 'option_index': '0', 'option_key': 'PROCEED'})
outcome won | continuity continuous | integrity complete | boundaries 75
```

The test replays the recording in-process through `Arbiter.ReplayStartedRun` and holds the replay to every declared boundary digest and to the recorder's own trace, step for step; that is the reproduction `gate` runs once its environment preflight has passed. The preflight itself refuses a headless recording by design (its patch roster is the headless host's, and it names no Runmobile patch), and `validate --show-rejections` cannot apply the unidentified-mod corruption to a run played with no mods, so `./scripts/arbiter gate` on this file fails at provenance and environment for the same reason it does on every headless recording and says nothing about the won run. The retail proof - a real win with the recorder attached, at Normal and at Instant fast mode - still needs the client slot.
