# Distribution: where an StS2 mod goes, and what that means here

This project is not published yet. This is the shape it has to fit when it is, and
the evidence for that shape, so the decision is not re-derived from folklore later.

## The channels

**Steam Workshop is the official, default channel.** MegaCrit added it in the
v0.107.1 major update (June 2026), describing it as *"now the official way to browse
and install mods, right in the Steam client."* It is the only channel with
per-branch versioning, automatic updates and cross-device sync. Scale, from Steam's
own API: BaseLib has 696,406 current subscriptions; Quick Restart 2 has 226,231.

**Nexus Mods is a genuine secondary, not a leftover habit.** The Slay the Spire II
section carries 938 mods with current content, and BaseLib has 274,292 downloads
there — roughly 40% of its Workshop reach. Its content skews toward character, skin
and art mods, several of which Steam's content descriptors make awkward; that is the
structural reason it persists. Worth a mirror. Not a primary.

**GitHub Releases is where tool-shaped mods actually live.** BaseLib's split tells
the story: 696k Workshop subscribers against ~22k release-zip downloads. Workshop
reaches players; GitHub reaches developers.

No other channel is worth building for: there is no StS2 mod manager, no Thunderstore
presence, no r2modman profile.

## What that means for this repository

**Do not build a multi-store publishing framework.** There are at most three targets
and two of them are a file upload.

**Keep the replay format storefront-neutral.** The parts that matter long-term — the
manifest schema, the canonical state projection, the cache key, the validator — live
in `Sts2PilotTrainer.Replay`, which depends on nothing: not the game assembly, not a
video pipeline, not a channel. A manifest verified today stays readable regardless of
where anything is eventually published, and the tests for all of it run on a machine
that does not own the game.

**The eventual published artifact carries the local arbiter it needs.**
It contains `Runmobile.json`, `Runmobile.pck`, `Runmobile.dll`, and the four project-owned libraries that the host uses: `Sts2PilotTrainer.Trainer.dll`, `Sts2PilotTrainer.Engine.dll`, `Sts2PilotTrainer.Replay.dll`, and `Sts2PilotTrainer.IO.dll`.
It also contains a platform-specific, self-contained `arbiter/` directory so `Share this run` can apply the real publication gate in fresh processes without a repository checkout or a separately installed .NET runtime.
Its one-resource pack supplies the 64×64 `mod_image.png` the game's mod list reads; `has_pck: true`, `dependencies: []`, and `affects_gameplay: false` remain the manifest's complete declaration.
`./scripts/package-mod.sh` produces the distributable archive with a self-contained preparation tool and `install.sh` beside the mod payload.
After extraction, `./install.sh` installs that payload without a repository checkout or a system .NET runtime.
That installer prepares a private, receipted copy of the player's own game assemblies under `arbiter/lib/` before atomically replacing the installed mod.
When publication runs, the installed arbiter routes its engine writes into a temporary workspace inside the profile-scoped Runmobile store.
The published archive contains no game assembly; the prepared copy is made locally from the installation during install.
It also contains no sharing endpoint.
Online sharing is enabled only when the profile-scoped `settings.json` explicitly names an absolute HTTPS `sharing_service_url` without embedded credentials, a query, or a fragment.
That configured service receives the complete replay manifest and the submission's name, description, display name, and CC0 consent only after the local publication gate passes; without it, no network request is made and the client says sharing is unavailable.
Everything else in this repository that could not go inside that archive — including the video tooling — is a build-time or proof-only concern and is kept out of the published mod.
See [dependencies](dependencies.md).

**One version, and it is the mod manifest's.**
`src/Sts2PilotTrainer.Mod/Runmobile.json`'s `version` field is the only version declaration for the assemblies the mod ships.
`Directory.Build.props` reads it and stamps every assembly here from it, so a release is one edit to one field and nothing can be left behind.
This matters past tidiness because a native recording names the build twice - once in its mod set, as the mod the game reported loaded, and once as `source.native.recorder_version` - and a reader deciding whether a recording is browsable and reproducible on their own patch is reading those strings.
They disagreed: nothing declared a version, so the assemblies carried .NET's default `1.0.0.0` and every recording written before this said `runmobile-recorder/1.0.0.0` beside a mod set saying `Runmobile 0.1.0`.
Those recordings are evidence of what happened and are not edited to match; `RunmobileVersion` is the one reader of the stamped value.
`VersionAgreementTests` and `RecorderVersionTests` assert it over every assembly of ours found beside the running test binary rather than a list they name, so a project added later is asked as soon as anything references it - the first suite covers the game-free set CI runs, the second the two that need the game.
Two versions are deliberately outside the arrangement.
`GodotStubs` is not ours: that assembly has to keep `GodotSharp`'s own identity for the game assembly's references to resolve.
`Arbiter.Version` in `Sts2PilotTrainer.Engine` is the headless arbiter's own version, written into every verification report as `arbiter_version`, and it stays independent because the CLI is a separate executable even though Runmobile packages it for local publication checks.
The consequence is intended and worth stating: bump the mod's version and `arbiter_version` still reads the arbiter's own until somebody bumps that too.

## What the installed mod writes

The mod is installed as `Runmobile` and writes in exactly one place: `user://Runmobile/`, inside the game's own user data directory, under the platform, account and profile scope the game resolved for itself - `user://Runmobile/steam/<account>/profile1/`.
That is where a player's own recordings, their progress through one and the derived boundary cache go.
`RunmobileStore` is the only writer in the mod and every path it is given is checked against that root; see [the in-game host](in-game-host.md).
Those scope identifiers stay local: nothing exported, uploaded or shared carries a platform directory, an account id or a profile number.

Nothing else is ever written.
Saves, profiles, progress, run history, settings, the game's installation and other mods' files are read-only inputs, and `scripts/protected-files.sh` is the repeatable measurement of that - a ledger before a session and a comparison after, with the `user://Runmobile/` subtree reported separately from everything that must not change.
The install directory is the one exception and belongs to the installer rather than to the running mod: `scripts/install-mod.sh` puts the file set there, and nothing inside the game process writes to it.

## Licensing posture

This project is MIT. It redistributes no game content and no video footage; see
`NOTICE` for what is included and what is deliberately excluded.

One licence check worth carrying forward: read the `LICENSE` file in a repository
rather than a GitHub badge or a README. At least one relevant project is unlicensed
on GitHub while publishing the same content under MIT on NuGet, and an absent licence
means all rights reserved.
