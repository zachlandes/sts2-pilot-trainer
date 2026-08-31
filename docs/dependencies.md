# Dependencies: what this needs, and what a player would need

Two different questions, kept apart on purpose. What the *proof* runs on is allowed
to be awkward. What a published mod asks a player to install is not.

## Tiers

| Tier | What is in it today | The rule |
|---|---|---|
| **End-user runtime** | **Nothing.** | Target zero. There is precedent at scale: Quick Restart 2 has 226,231 Workshop subscribers and declares no dependencies. Nothing here needs a mod framework — Harmony ships with the game. |
| **Bundled into a release** | Nothing yet. | Only small, permissively licensed, build-time files. A runtime mod DLL can never be bundled: it would collide with the player's own copy. |
| **Optional integrations** | None wired. STS2MCP and RunReplays are the plausible candidates. | Detect at runtime, degrade silently, never declare in a manifest's `dependencies`. A hard dependency turns an integration into a requirement. |
| **Developer / build-time** | .NET 9 SDK. Mono.Cecil, for the one IL patch. xunit. | Lives in the build files. Never in a user-facing instruction. |
| **Proof-only** | The vendored Godot stubs; the prepared, IL-patched copy of the game assembly; `yt-dlp` and `ffmpeg` for reading frames; the hand transcription. | Must not appear on any user path. Enforced by the project boundary: nothing a published mod would ship references `tools/`. |

The line that must not blur: this proof runs on a machine with mods installed, a
patched copy of the game assembly, and a video pipeline. **None of that is a user
requirement**, and the way to keep it that way is a directory boundary plus a check
on what a release archive actually contains — not an intention.

## Notably not depended on

**BaseLib** is the one dependency that could earn its place: MIT, 696,406 Workshop
subscriptions, and it ships inside a day of each game patch. It is not taken,
because nothing here needs it. The gate for taking it later is to name the feature,
confirm it is not already in `sts2.dll` — whose `Modding` namespace is richer than
its reputation — and only then add it.

**Hindsight** is not used and should not become a user-facing dependency. It is,
however, present in the source creator's environment, and it is the one mod that can
invalidate a reconstruction — so the ingestion gates check for its fingerprints. See
[the resumed-run problem](environment-identity.md#the-resumed-run-problem). It is
Apache-2.0 and well maintained for its size, but it has roughly 374 GitHub release
downloads and 655 on Nexus against BaseLib's hundreds of thousands, no Workshop
presence, a one-person bus factor, and no declared minimum game version. The copy
installed on this machine (v0.2.1) does not bind against v0.111.0 at all; the
current v0.3.3 does.

It is also solving a different problem. Hindsight restores a saved floor-entry
snapshot. This project replays an ordered history from run start and derives the
snapshot. The difference is precisely what the proof turns on, so adopting it would
not have shortened the work.

## Third-party code actually included

`third_party/godot-stubs`, from [wuhao21/sts2-cli](https://github.com/wuhao21/sts2-cli)
at commit `d11aa88`, MIT. Audited before use: no network access, no credential
handling, no process spawning, and no writes outside its own tree. Its `setup.sh`
copies game assemblies into a local directory and patches only that copy. See
`NOTICE` and `third_party/godot-stubs/CHANGES.md`.

Its own caller does **not** compile against v0.111.0 — four call sites have drifted —
which is why this project vendors the stubs and writes its own binding rather than
forking the CLI.

## Deliberately not included

- **Game assemblies, localization tables, art.** MegaCrit's property. The bootstrap
  copies them from the player's own installation at build time into a gitignored
  directory. This project cannot be used without owning the game.
- **Video footage.** No frames, no clips, no stills. Only facts read from the video,
  with the public video id and timestamps that let anyone re-check them.
