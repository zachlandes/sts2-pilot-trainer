# Runmobile library in the retail client

The supported installer built and installed this branch for the v0.111.0 retail client.
The client was launched directly with `--force-steam=off`, without invoking Steam.
These captures replace the earlier screenshots rather than reusing them.
The commands below start in the retail Compendium with the game fullscreen on a 1512 × 982-point display.

```bash
screencapture -x runmobile-compendium-parchment.png
```

```output
```

![Runmobile destination in Compendium](runmobile-compendium-parchment.png)

```bash
cliclick c:766,720; sleep 2; screencapture -x runmobile-library-parchment.png
```

```output
```

![Others with the compatibility header, character portrait, and Act 1](runmobile-library-parchment.png)

```bash
cliclick c:538,326; sleep 2; screencapture -x runmobile-library-mine.png
```

```output
```

![Mine with the run strip and profile retention summary](runmobile-library-mine.png)

```bash
cliclick c:390,326; sleep 1; cliclick c:958,585; sleep 2; screencapture -x runmobile-library-open-run.png
```

```output
```

![Opened run with contained explanatory text and sized relic icons](runmobile-library-open-run.png)

The game-present Mod, Replay, and Trainer suites passed without skips.
The concurrent Arbiter run hit its session timeout; an isolated rerun passed with a bounded thirty-minute budget and no skips.
The publication gate returned PUBLISHABLE.
[The local check record](runmobile-library-game-present-checks.json) names every environment-gated test and its executed cases.
