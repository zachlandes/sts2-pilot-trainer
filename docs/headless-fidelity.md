# Running the real engine headless, and what that costs

The arbiter loads the actual shipped `sts2.dll` and drives the actual `RunManager`.
It does that in a plain console process with no Godot scene tree, no renderer, no
audio, no input and no frame loop. This is the list of everything that had to be
changed or stood in for to make that work, and the argument — with evidence where
there is evidence — that none of it changes what the game decides.

Nothing here is hidden from the output: every verification report carries these as
caveats, on passes as well as failures.

## The shape of the thing

- `third_party/godot-stubs` compiles to `GodotSharp.dll`: managed no-op stand-ins for
  the Godot API surface the game links against. Vendored from `wuhao21/sts2-cli`
  (MIT), plus a small addition of our own. See its `CHANGES.md`.
- `tools/Sts2PilotTrainer.Bootstrap` copies the player's own installed assemblies
  into `build/lib` and applies one IL patch to the **copy**. The installation is
  hashed before and after and the run fails if a byte moved.
- `src/Sts2PilotTrainer.Engine` is the only project that knows anything about a
  specific game version. When the game ships a new build, it is the only one that
  should need to change.

## What is neutralised, and why

### One IL patch, applied to the private copy

`CombatManager.WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction` returns a
completed task.

The host drains the game's action queue inline on a synchronous synchronization
context, so the queue is already empty by the time this wait is awaited. Left
intact, the await never resumes — there is no frame loop to pump it. The patch
changes *when the caller resumes*, not which actions ran or which RNG streams
advanced.

The bootstrap tool **requires** this patch to match at least one site. A patch that
silently stops matching is version drift, and the only safe response is a loud
failure.

### Runtime patches, applied with Harmony

Harmony is what the game itself ships and loads for mods, so this is the supported
mechanism rather than an unsupported hook.

| Patched | Why |
|---|---|
| `Cmd.Wait`, `TalkCmd.Play` | Animation sleeps and speech-bubble effects. Both block or throw with no scene tree. |
| `RunManager.FadeIn/FadeOut/ClearScreens/UpdateRichPresence` | Screen transitions and platform presence. Pure presentation. |
| `PreloadManager.Load*Assets` | Texture, audio and animation preloading. There is no renderer to want them, and the loaders dereference stub properties while assembling their asset lists. |
| `SaveManager.SaveRun`, `SaveProgressFile`, `SavePrefsFile`, `SaveProfileFile` | **The player's save directory is a read-only input.** The run is created with `shouldSave: false`, but the engine still reaches for the save subsystem on room entry. |
| `LocManager.GetTable`, `LocString.GetFormattedText/GetRawText`, `LocTable.*` | Localization is stubbed with no data at all — see below. |

### The headless flag

`TestMode.IsOn` is set. This is the switch the game's own automated tests use, and
the only supported way to make room, card, creature and banner constructors return
null instead of loading a scene.

It is **not** free of gameplay reach, and this is the one place where care was
needed. `RunManager.ShouldApplyTutorialModifications` reads, in order: an override,
then this flag (returning false when it is on), then the game mode — and for a
standard run in retail it returns **true, always**, not only on a first run. So
switching the headless flag on silently disables a behaviour every standard retail
run has, and `GenerateRooms` uses it.

The host therefore sets `RunManager.ForceDiscoveryOrderModifications` back to true
for standard runs, which is exactly what retail computes for them. Daily and custom
runs return false from the same method and get false here.

That was found by reading the method, not by noticing a symptom. The remaining
readers of the flag are in the presentation namespace or skip animation waits.

### Localization: stubbed empty

The real translation tables live inside the game's Godot resource pack, which a
headless process has no reader for. Rather than extract and redistribute MegaCrit's
text — which this project will not do — every lookup is patched to succeed and
return its own key.

This is safe because nothing the project compares is localized: the canonical state
is model ids and numbers, and display text is on the canonical form's excluded list
by design. It costs legibility in a raw dump, which is what model ids are for.

**One honest caveat.** The game can pick a random string from a table
(`LocString.GetRandomWithPrefix`), and against empty tables there is nothing to pick
from. If any gameplay path drew from a run-persistent stream to choose flavour text,
the stub would move that stream. The proof this milestone runs would catch such a
divergence — it compares generated map topology and combat outcomes against a real
video — but that is a bound, not a proof of absence.

### No mods

The mod loader is declared finished with nothing loaded. This is deliberate and it
is what makes the content hash meaningful: the hash this host reports is the base
game's, so a match against a video's overlay says the video's environment agreed
with the base game about what content exists.

### Writes are confined

`OS.GetUserDataDir` and `ProjectSettings.GlobalizePath` are routed through a sandbox
directory the process owns, and any filesystem operation that resolves inside a
Steam or Slay the Spire 2 path throws rather than proceeding. The player's install
and saves are inputs, and the engine has no idea that is the arrangement.

## The evidence that generation is unaffected

Arguments about which patches are "only presentation" are worth exactly as much as
the measurements behind them. These are the measurements.

**Act 1 map topology matches the source video exactly** — all 61 transcribed nodes
across 15 of the map's 16 rows, read from five frames at source resolution. Map
generation runs through the act model, the room set, and the path pruning and
post-processing passes. It comes out identical to what a retail client on a modded
install produced.

**The headless experiment matched 21 independently observed VOD values** — including the enemy state, ordered hand, pile counts, energy, block, and turn outcome.
This is evidence that the experiment followed the same path through the opening turn, not proof of exact source reproduction: the source had three mods loaded and no controlled parity A/B exists.

**The replay machinery has independent synthetic evidence** — a mechanically generated fixture uses a seed and action sequence absent from the VOD artifacts and pins its engine-produced checkpoints.
Fresh-process determinism, corruption rejection, and snapshot restore are exercised against that fixture, so those checks do not borrow their expected values from the ineligible VOD trace.

## What is still not established

- **This is not the retail client.** Everything above is agreement at points a video
  could show. It is strong evidence and it is not the same as running the game.
- **The source environment had three mods loaded and this one has none.** The content
  hash matches, which rules out gameplay-declared content differences and nothing
  else. See [environment identity](environment-identity.md).
- **Unlock state is assumed complete.** It demonstrably changes generated content,
  and it is not observable from a video. See the same document.
- **Only the first turn of the first combat is covered.** Every claim here is about
  the part of the run that was transcribed.
