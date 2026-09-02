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
It contains `CombatTrainer.json`, `CombatTrainer.dll`, and the four project-owned libraries that the host uses: `Sts2PilotTrainer.Trainer.dll`, `Sts2PilotTrainer.Engine.dll`, `Sts2PilotTrainer.Replay.dll`, and `Sts2PilotTrainer.IO.dll`.
It remains DLL-only in the game's packaging terms: `has_pck: false`, `dependencies: []`, `affects_gameplay: false`.
Those four libraries ship with the mod; they are not runtime dependencies a player installs separately.
Everything in this repository that could not go inside that archive — the prepared game-assembly copy, the video tooling, the bootstrap — is a build-time or proof-only concern and is kept out of the projects a mod would ship.
See [dependencies](dependencies.md).

## Licensing posture

This project is MIT. It redistributes no game content and no video footage; see
`NOTICE` for what is included and what is deliberately excluded.

One licence check worth carrying forward: read the `LICENSE` file in a repository
rather than a GitHub badge or a README. At least one relevant project is unlicensed
on GitHub while publishing the same content under MIT on NuGet, and an absent licence
means all rights reserved.
