# External dependency audit

This record covers repositories inspected for replay architecture or optional integration.
No code from RunReplays, Hindsight, or STS2MCP is included in this repository.
The only adapted code is the separately attributed Godot stub layer described in `NOTICE`.

## BaseLib v3.4.5

- **Provenance and maintenance:** `Alchyr/BaseLib-StS2`, tag `v3.4.5`, commit `22757933ba10adc4322a628519a233a567507d87`, released 2026-08-14.
- **License:** MIT.
- **Entry point:** `BaseLibMain.Initialize` registers Godot scripts and config, applies targeted Harmony patches, then calls `TryPatchAll` over the assembly.
- **Installation and build behavior:** release installation copies the DLL, PCK, and manifest into the game's mods directory.
The project build also copies outputs into that directory, and its optional Godot publish target executes Godot and can copy upload artifacts.
This project does not run those build targets; the parity probe uses the published DLL as a read-only input from an isolated worktree directory.
- **Network, filesystem, and credentials:** no network or credential access was found in the C# source.
Config code reads and writes mod configuration, and the optional Harmony diagnostic writes a patch dump to a configured path.
Linux initialization loads `libgcc_s.so.1` with `dlopen`.
- **Trust decision:** suitable as a bounded proof-only input at its exact release hash, not accepted as an end-user dependency and not enough to establish source-environment parity by name alone.

## RunReplays

- **Provenance and maintenance:** `boardengineer/RunReplays`, inspected at `b0d2302ee69bf2ad735e0b6b51aea02408e9ef62`, last commit 2026-07-07.
- **License:** MIT.
- **Entry points and behavior:** a game mod records commands through Harmony patches, presents replay controls in the main menu, and dispatches recorded commands against game state.
- **Installation behavior:** the README instructs users to copy the DLL, PCK, and manifest into the game mods directory.
- **Network, filesystem, and credentials:** no network or credential path was found.
It writes replay logs and diagnostics, reads saved runs, and can copy a selected historical save over the active run-save path before loading it.
- **Trust decision:** its command vocabulary and dispatcher informed the action seam.
Its save replacement and in-client UI are incompatible with this project's read-only-input and headless-publication boundaries, so it is not incorporated.

## Hindsight

- **Provenance and maintenance:** `Landmaster/Hindsight`, inspected at `5931449991926cb915106ba93d34312ad8be0f0c`, last commit 2026-08-19.
- **License:** Apache-2.0.
- **Entry points and behavior:** Harmony patches save each floor entry and add run-history interaction that initializes a run from a selected saved floor.
- **Installation behavior:** the release archive is unpacked into the game mods directory and declares BaseLib as a dependency.
- **Network, filesystem, and credentials:** no network or credential access was found.
It reads and writes save data under a `hindsight` subtree and deletes older retained snapshots according to configuration.
- **Trust decision:** maintained and license-compatible, but it restores saved state rather than replaying history from run start.
It remains a named source-environment risk and is not a runtime dependency.

## STS2MCP

- **Provenance and maintenance:** `Gennadiyev/STS2MCP`, inspected at `55e064850a68f3b4cde7e5fd525bf9b2dec4e885`, last commit 2026-07-29.
- **License:** MIT.
- **Entry points and behavior:** the mod starts an HTTP listener on localhost, exposes game state and actions, and optionally connects through a Python MCP server.
- **Installation behavior:** users copy the DLL and manifest into the game mods directory; the optional MCP path uses `uv` to create an environment from a locked dependency set.
- **Network, filesystem, and credentials:** localhost networking is the product surface.
The mod writes configuration, reads profile and run-history files, and can switch or delete profiles through game actions.
No credential collection was found, but the server grants an external process game-control and profile-data access.
- **Trust decision:** useful evidence for a replaceable control adapter, but its network server, profile access, and older tested game build make it unsuitable for the exact-replay spine.
