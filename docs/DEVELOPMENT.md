# Development Guide

## Current names and source files

The project and plugin display name are **Overcooked2RecipeViewer**. All project
C# files use the `Overcooked2RecipeViewer` namespace. The BepInEx entry point is
`Overcooked2RecipeViewer.RecipeViewerPlugin`, in `RecipeViewerPlugin.cs`.
The current plugin version remains `0.8.3`.

| File | Responsibility |
| --- | --- |
| `RecipeViewerPlugin.cs` | Plugin entry point, level hooks, recipe collection, configuration and toolbar |
| `RecipeBoard.cs` | Recipe cards, sorting, scrolling, zoom, dragging and export capture |
| `RecipeSortMetadata.cs` | Cookware metadata and conservative fallbacks |
| `RecipeLayoutStore.cs` | Per-level drafts and saved layouts |
| `RecipeUiText.cs` | Chinese/English UI and Configuration Manager labels |
| `PngStreamWriter.cs` | PNG encoding |
| `ClipboardImage.cs` | Windows clipboard support |

Some identifiers intentionally retain their old spelling for compatibility:

- Plugin GUID: `io.github.overcooked2.recipepreview`; the existing configuration
  file is `BepInEx\config\io.github.overcooked2.recipepreview.cfg`.
- Shortcut keys: `Keyboard shortcuts` / `Toggle all recipe images`; translated
  display labels do not change these configuration keys.
- Layout storage: `%APPDATA%\Overcooked2RecipePreview\layouts.txt`, with local
  application data as a fallback. The format header remains `RecipeLayoutV1`.
- PNG export directory: `BepInEx\RecipePreviewExports`.
- `Overcooked2RecipePreview.dll` in the deployment script identifies an old
  installation to migrate; it is not the current output filename.

These are not stale namespace, class or display names. Changing them without a
migration could lose access to existing settings or layouts.

## Build

Use Windows PowerShell 5.1 or newer, .NET Framework 3.5 C# compiler tools, and a
local Overcooked! 2 installation with BepInEx 5. There is no `.csproj` or `.sln`
in this repository: [`build.ps1`](../build.ps1) compiles the seven source files
directly with `csc.exe`.

From the repository root:

```powershell
.\build.ps1 -GameDir '<game-directory>'
```

`-GameDir` is the directory containing `Overcooked2_Data` and `BepInEx`. Both
scripts can also read it from the session environment variable `OC2_GAME_DIR`.
The compiler defaults to the Windows .NET Framework 3.5 installation;
`-CompilerPath '<path-to-csc.exe>'` overrides it.

The build reads game assemblies from `Overcooked2_Data\Managed` and BepInEx /
Harmony from `BepInEx\core`; see the script for the full reference list. It uses
`/noconfig`, `/nostdlib+` and `/codepage:65001`, with the game's `mscorlib.dll`,
`System.dll` and `System.Core.dll` to preserve the older runtime target.
Do not commit or redistribute game assemblies, BepInEx binaries or game assets.

Output: `artifacts\Overcooked2RecipeViewer.dll`. Building does not deploy it.

## Installation and deployment

BepInEx scans `plugins` and its subdirectories. The recommended installation is:

```text
<game-directory>\BepInEx\plugins\Overcooked2RecipeViewer\Overcooked2RecipeViewer.dll
```

Close the game before deployment. After a successful build, preview or perform
installation using [`deploy.ps1`](../deploy.ps1):

```powershell
.\deploy.ps1 -GameDir '<game-directory>' -WhatIf
.\deploy.ps1 -GameDir '<game-directory>'
```

The default destination is the recommended subdirectory above. `-PluginDir`
can select another directory, but it must be a subdirectory of this game's
`BepInEx\plugins`; paths through directory links are rejected. The script checks
for nonempty build output and duplicate installations, copies only this mod's
DLL and verifies its SHA-256 hash. `-WhatIf` runs validation without writing.
The script does not start or stop the game.

### Migrating an old DLL

Manual deletion is not required for a supported migration. Explicitly select
`-ReplaceLegacy` and the folder that currently contains the old DLL:

```powershell
.\deploy.ps1 -GameDir '<game-directory>' -PluginDir '<existing-plugin-subdirectory>' -ReplaceLegacy -WhatIf
.\deploy.ps1 -GameDir '<game-directory>' -PluginDir '<existing-plugin-subdirectory>' -ReplaceLegacy
```

Migration requires exactly one regular `Overcooked2RecipePreview.dll` under the
game's plugin tree, located in the selected folder. There must be no existing
Viewer DLL there and no Viewer copy in another plugin folder. Multiple legacy
copies, links or an ambiguous installation are refused; the script does not
clean unrelated directories or delete other mods.

The script backs up the legacy DLL in the system temporary directory, replaces
its contents, verifies the hash and renames it to `Overcooked2RecipeViewer.dll`
in the **same selected folder**. It does not move that folder to the recommended
default location. A copy/rename failure triggers an attempt to restore the old
DLL; if rollback fails, the backup is retained and its path is reported.
Successful migration removes the temporary backup. Settings and layouts remain
untouched, and two active copies are not intentionally installed.

Without `-ReplaceLegacy`, finding a legacy DLL blocks deployment. Select the
correct migration folder, or resolve an unsupported/duplicate installation
manually before retrying.

## Development checks

Read the existing implementation before editing, keep changes small, and build
after changes. Preview cards must not enter the real order list or alter order
generation, timers, scoring or level data. Cookware sorting uses game metadata;
unknown data should degrade conservatively rather than add dish-name guesses.

Runtime testing is performed by the user. For a runtime change, provide the
purpose, exact steps, expected result and observations to report. Check a normal
level, dynamic recipe phases and scripted levels when relevant; include preset,
dragging, saved-layout and PNG export regression checks. On failure, request the
level/player count, reproduction steps, relevant `BepInEx\LogOutput.log` excerpt
and screenshots where helpful.
