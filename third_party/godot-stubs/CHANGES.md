# Changes from upstream

Vendored from [wuhao21/sts2-cli](https://github.com/wuhao21/sts2-cli) `src/GodotStubs`,
commit `d11aa88`, fetched 2026-08-30. MIT licensed; see `LICENSE`.

## Why vendored rather than referenced

These stubs are a mechanical mirror of the Godot C# API surface that
`sts2.dll` links against. They are the one part of the headless route that is
expensive to re-derive and cheap to keep: they compile unchanged against
Slay the Spire 2 `v0.111.0`, which was verified before vendoring.

Upstream's own caller (`RunSimulator.cs`) does *not* compile against v0.111.0 —
four call sites have drifted. That drift is confined to the caller, not to
these stubs, which is exactly why this project vendors the stubs and writes its
own binding (`src/Sts2PilotTrainer.Engine`) instead of forking the CLI.

Taking the stubs as a pinned copy rather than a submodule also keeps a
distributable mod free of a build-time dependency on a third-party repository.

## Modifications

| File | Change |
|---|---|
| `Core.cs` | Two method bodies. `OS.GetDataDir` / `OS.GetUserDataDir` return the sandbox root instead of `"."`, and `ProjectSettings.GlobalizePath` routes through `HeadlessSandbox.Globalize` instead of returning its argument. Upstream's versions let the engine write into the process working directory; this project treats the player's install and saves as read-only inputs and needs writes confined to a directory it owns. |
| `UI.cs` | `DirAccess` is `partial`, so members this project adds live in its own file rather than in the vendored snapshot. Its three directory-creation methods route through `HeadlessSandbox` before touching the filesystem, preventing an absolute game or save path from bypassing the host's read-only boundary. |
| `GodotStubs.csproj` | References the project's shared path-containment owner so the sandbox uses the same real-path and symbolic-link boundary as bootstrap and snapshot caches. |
| `UI.cs` | `Control`, `Label` and `TextureRect` are `partial`, so the members this project adds live in its own file rather than in the vendored snapshot. `Button` derives from `BaseButton`, which owns the `Pressed` event, as Godot's own hierarchy does: code compiled against the real GodotSharp emits `BaseButton.add_Pressed`, which upstream's flat `Button : Control` could not resolve at load. |
| `UI.cs` | `BaseButton` and `Button` are `partial`, so `Disabled` and `Flat` - which the transport sets on a control it draws but is not offering, and on the hit areas it lays over text it has already drawn - live in this project's own file rather than in the vendored snapshot. |
| `Sts2PilotTrainerAdditions.cs` | **Added by this project.** Supplies `Godot.StringExtensions`, which upstream does not stub. Its five members (`GetBaseDir`, `GetFile`, `PathJoin`, `Capitalize`, `ToSnakeCase`) are the complete set `sts2.dll` v0.111.0 references, enumerated from the assembly rather than guessed. Without the type, every type mentioning it fails to load and the save subsystem does not initialise. It also owns `HeadlessSandbox` and the added mutating `DirAccess` methods so every engine write resolves through the shared real-path containment boundary beneath an isolated worktree sandbox rather than a temporary directory or a symbolic link into the installed game or player saves. It further supplies the small Control surface the mod's result panel and playback transport draw with - `VerticalAlignment`, the label and texture-rect layout members, `StyleBox`/`StyleBoxFlat`, the three theme-override methods, `BaseButton.Disabled`, `Polygon2D` and `Control`'s hover and focus signals - each a member of the real GodotSharp the mod compiles against, stubbed so both surfaces load in the mod's game-free tests. `Polygon2D` is there because the transport's glyphs are the mod's own drawn art rather than the game's, the game shipping no playback iconography; the hover and focus signals are there because the transport shows a tooltip on one and the game's own reticle on the other, and both are asserted without a game. |
| `ExtraGodotTypes.cs`, `Sts2PilotTrainerAdditions.cs` | `Font` is `partial`, and `Font.GetMultilineStringSize` is added in this project's own file. The transport sizes every panel that carries a sentence by measuring the text once it has wrapped, rather than by counting the newlines in it; without the method the runtime cannot resolve the call and fails before the transport's own "no font to measure with" check runs. The stub's estimate mirrors the `GetStringSize` beside it rather than Godot's real shaping, which needs a font. |

Record every future divergence here, with the reason. When upstream moves, diff
against the new revision rather than re-deriving: this table is what makes that
diff readable.
