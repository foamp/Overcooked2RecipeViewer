# Development guide

This document describes the implementation and compatibility decisions behind
Overcooked2RecipeViewer. User instructions are in the [README](../README.md).

## Source map

| File | Responsibility |
| --- | --- |
| `RecipeViewerPlugin.cs` | BepInEx entry point, level hooks, recipe collection, toolbar and configuration |
| `RecipeBoard.cs` | Native recipe cards, scrolling, zoom, row layout, drag interactions and export capture |
| `RecipeSortMetadata.cs` | Cooking equipment metadata and conservative scene fallbacks |
| `RecipeLayoutStore.cs` | Per-level drafts, saved layouts and local persistence |
| `RecipeUiText.cs` | English/Chinese UI and Configuration Manager display labels |
| `PngStreamWriter.cs` | PNG scanline encoding without a full-height GPU texture |
| `ClipboardImage.cs` | Windows clipboard bitmap support |
| `build.ps1` / `deploy.ps1` | Local compilation / explicit single-DLL installation |

## Level data and hooks

The Harmony prefix on `PlayerLobbyFlowroutine.LoadKitchen(int, string, Sprite,
GameSession.GameLevelSettings)` reads
`GameLevelSettings.SceneDirectoryVarientEntry.LevelConfig` before the loading
screen starts. This selects the level variant for the player count.

A second prefix on `LoadingScreenFlow.LoadScene(...)` checks
`GameSession.LevelSettings` for custom level flows that bypass the lobby hook.
The capture logic deduplicates repeated captures of the same configuration and
scene within its capture window. This fallback does not guarantee compatibility
with every third-party level mod.

The collector reads the public `LevelConfigBase.GetAllRecipes()` entry point and
the relevant configuration data, including:

- Campaign round recipe lists (`RoundData.m_recipes.m_recipes`).
- Dynamic phase recipe lists (`DynamicRoundData.Phases[*].Recipes`).
- Scripted orders (`ScriptedRoundData.m_manualOrder`).
- Single-player orders (`SinglePlayerLevelConfig.RecipeOrder`).

Recipes are deduplicated by Unity object instance ID. Logs retain the node's
resource name, `m_uID` and node type for diagnosis.

## Display and interactions

The board clones the game's `RecipeWidgetUIController` / `RecipeWidgetTile`
visuals. It does not register preview cards with `RecipeFlowGUI.AddElement` or
modify the real order list, timers, scores, weights or level data.

Cloned `UI_Move` offset materials are disabled and the green order progress
indicator is hidden in the preview. Card sizes come from rendered bounds rather
than a fixed column width. Dish alignment uses the paper frame bounds and image
transparency. Scroll bounds are recalculated after rendering.

Layout uses lists of rows containing cards. Item drag and row drag are distinct
states. The narrow insertion zone between adjacent rows creates a new row; the
top and bottom edges retain normal cross-row drop behavior. Empty rows are
removed after edits. Row handles remain fixed while scrolling horizontally.

Zoom changes display coordinates only, ranges from 50% to 200%, and is retained
within a running game session. It is disabled during dragging, export and an
open preset menu. Export captures at the normal scale and restores the previous
zoom and scroll position afterwards.

## Sorting

- **Original order** uses the collected recipe sequence and the board's normal
  row construction.
- **Auto arrange** retains the existing resource-name grouping rules. These are
  legacy presentation heuristics, not authoritative dish-type metadata.
- **Name A-Z** compares resource names case-insensitively and uses original index
  to break ties. It does not sort translated dish labels.
- **Cookware** traverses cooked and mixed composite nodes. Known
  `CookingStepData` asset names identify equipment such as `DeepFatFryer`,
  `FryingPan`, `Pot`, `Steamer` and `OvenTray`. Unknown assets fall back to live
  station links and recognizable container components.

Mixing is preparatory when cooking steps also exist: mixed oven dishes group
with ovens, and mixed steamed dishes group with steamers. Multiple cooking
equipment types form a separate combined group. Unresolved metadata stays in a
broad or unknown group; no new dish-name inference is added. There is no
authoritative dish-type or primary-ingredient field used by this mod.

On opening a board, a valid last edited draft takes precedence over Cookware.
Selecting a built-in preset or saved snapshot does not erase that draft.

## Compatibility identifiers

The public project name, plugin display name and new DLL name are
`Overcooked2RecipeViewer`. The runtime version remains `0.8.3`; this repository
cleanup does not introduce a gameplay feature release.

The following historical identifiers intentionally remain unchanged:

| Identifier | Reason |
| --- | --- |
| Plugin GUID `io.github.overcooked2.recipepreview` | Keeps the BepInEx config file and Harmony identity stable |
| Config section `Keyboard shortcuts` and key `Toggle all recipe images` | Preserves an existing configured shortcut |
| Namespace `Overcooked2RecipePreview` and class `RecipePreviewPlugin` | Avoids unnecessary runtime type identity changes |
| `%APPDATA%\Overcooked2RecipePreview\layouts.txt` | Keeps existing drafts and snapshots accessible without migration |
| Layout header `RecipeLayoutV1`, level/recipe keys and `Saved N` names | Keeps stored layout records and language-independent identities compatible |
| `BepInEx\RecipePreviewExports` | Keeps the existing export location and folder button behavior |
| `RecipePreview_*` cloned object names and recipe-preview log marker | Keeps diagnostics and internal scene naming stable |

The entry file was renamed to `RecipeViewerPlugin.cs`, while the class identity
inside it remains unchanged. Install only one DLL with this plugin GUID. An old
`Overcooked2RecipePreview.dll` must be removed manually when upgrading; scripts
do not delete it or any other installed file.

## Persistence and localization

Layouts are stored outside the game directory, under the user's roaming
application data directory (with local application data as a fallback).
`RecipeLayoutStore` uses a versioned text format and only framework types; no
additional serialization dependency is shipped. Dragging saves the draft;
named snapshots are separate. Deleting a snapshot preserves other snapshots,
the draft and other levels.

`Localization.GetLanguage()` selects Chinese UI for Chinese and Traditional
Chinese game settings, and English UI otherwise. Configuration Manager receives
translated display labels through optional reflected attributes, while config
keys and stored layout names remain stable.

## PNG and clipboard

PNG export preserves the complete row structure, including offscreen cards.
Individual card captures are composed into a light background and streamed as
scanlines, avoiding a GPU texture as tall as the final image. Dimension and
capture limits can still prevent oversized exports. Windows clipboard transfer
can use a reduced full-image bitmap; a clipboard failure does not invalidate a
successfully saved PNG. Export never submits a physical print job.

## Building and deployment

Use Windows PowerShell 5.1 or newer, .NET Framework 3.5 compiler tools, and local
copies of the game's assemblies and BepInEx 5. See `build.ps1` for the complete
reference list. The existing environment was compiled against BepInEx
`5.4.23.1` and Harmony `2.9.0.0`; other versions are not automatically certified.

The build uses `/noconfig`, `/nostdlib+` and `/codepage:65001`, with explicit game
copies of `mscorlib.dll`, `System.dll` and `System.Core.dll`. This keeps the
older Mono/CLR v2 target and avoids silently compiling against newer framework
APIs unavailable in the game.

```powershell
.\build.ps1 -GameDir '<game-directory>'
.\deploy.ps1 -GameDir '<game-directory>' -WhatIf
.\deploy.ps1 -GameDir '<game-directory>'
```

Both scripts also accept the session environment variable `OC2_GAME_DIR`.
`-CompilerPath` selects another compiler with the same target compatibility.
The output is `artifacts\Overcooked2RecipeViewer.dll`. Deployment is separate
from building. `-PluginDir` may select an existing plugin subdirectory inside
the chosen game's `BepInEx\plugins` directory. Deployment validates the source,
destination, duplicate installations and file hashes, and copies only this
project's DLL. It never starts the game or cleans unrelated files.

Local `AGENTS.md` instructions and machine-specific settings are ignored and
are not part of the current published tree. Earlier commits still contain the
previous instructions and local paths; removing a tracked file does not erase
Git history. No history rewrite is performed by this cleanup.

## Manual regression checks

Runtime checks are performed by the user. Useful coverage includes a normal
story level, a level with changing recipe phases, and a scripted/tutorial level.

1. Open/close using Insert and verify a previously configured shortcut still
   works; confirm the renamed plugin display entry.
2. Check that original orders and scores behave normally.
3. Select all presets, especially fryer, pan, pot, steamer, oven and mixed
   equipment groups. Check that unknown modded steps degrade conservatively.
4. Drag within/across rows, insert a new row and reorder a whole row while
   scrolling both axes. Check empty row cleanup and zoom at 50%/100%/200%.
5. Restore a pre-existing draft after reopening and restarting. Save, select and
   delete a named snapshot without losing other layouts.
6. Export a wide/tall board and confirm row order, offscreen cards, normal export
   scale and clipboard behavior. Confirm the previous zoom/scroll is restored.
7. Switch Chinese and another game language; check toolbar, preset, status and
   Configuration Manager labels.

When reporting a failure, include the level/player count, mod version, exact
actions, observed result, screenshot when relevant and the corresponding
`BepInEx\LogOutput.log` excerpt. Remove personal information before posting.
