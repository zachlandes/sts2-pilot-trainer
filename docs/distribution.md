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

**The eventual published artifact is small.**
It contains `Runmobile.json`, `Runmobile.dll`, and the four project-owned libraries that the host uses: `Sts2PilotTrainer.Trainer.dll`, `Sts2PilotTrainer.Engine.dll`, `Sts2PilotTrainer.Replay.dll`, and `Sts2PilotTrainer.IO.dll`.
It remains DLL-only in the game's packaging terms: `has_pck: false`, `dependencies: []`, `affects_gameplay: false`.
Those four libraries ship with the mod; they are not runtime dependencies a player installs separately.
Everything in this repository that could not go inside that archive — the prepared game-assembly copy, the video tooling, the bootstrap — is a build-time or proof-only concern and is kept out of the projects a mod would ship.
See [dependencies](dependencies.md).

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
