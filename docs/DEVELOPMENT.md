# Development Guide

## Current names and source files

The project and plugin display name are **Overcooked2RecipeViewer**. All project
C# files use the `Overcooked2RecipeViewer` namespace. The BepInEx entry point is
`Overcooked2RecipeViewer.RecipeViewerPlugin`, in `RecipeViewerPlugin.cs`.
The current release version is `0.9.0`.
Assembly/file versions are `0.9.0.0`; the informational/plugin version is `0.9.0`.

| File | Responsibility |
| --- | --- |
| `RecipeViewerPlugin.cs` | Plugin entry point, level hooks, recipe collection, configuration and toolbar |
| `RecipeBoard.cs` | Recipe cards, sorting, scrolling, zoom, dragging and export capture |
| `RecipeExport.cs` | Export settings/configuration tags, alpha crop, layout plans, disk cache and shared RGBA scanlines |
| `RecipeCardRenderer.cs` | Isolated camera/canvas capture of finalized original cards |
| `RecipeExportPreview.cs` | Plugin export dialog, settings persistence and cancellable work |
| `AssemblyInfo.cs` | Assembly, file and informational versions |
| `RecipeSortMetadata.cs` | Cookware metadata and conservative fallbacks |
| `RecipeLayoutStore.cs` | Per-level drafts and saved layouts |
| `RecipeUiText.cs` | Chinese/English UI |
| `RecipeUiSettings.cs` | Interface preferences, four palettes and responsive geometry without Unity native calls |
| `RecipeUiAppearance.cs` | Live interface settings dialog, rounded styles and debounced persistence |
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
in this repository: [`build.ps1`](../build.ps1) compiles thirteen source files
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

## v0.9.0 export pipeline

`RecipeBoard` captures complete native cards at fixed 1x: one output pixel per
original card UI unit, independent of screen size, board zoom and scroll. It
does not resize individual sprite elements to their atlas dimensions or invent
detail beyond the original resources. Capture preflight checks per-card texture
and total cache budgets; exact image dimensions are shown only after capture.

`RecipeExport.Add` crops each capture to all nonzero-alpha pixels, retaining even
alpha=1 shadows. Top-down placement is adjusted for bottom-up pixel cropping.
`CreatePlan` produces copied placements. The internal legacy Current mode keeps
relative positions for compositor regressions but is no longer a UI choice;
`ExportSettings.Normalize` migrates it to Standard. Capture records `ExportCard.Row` directly from
the viewer's `_rows`, before alpha crop changes individual top coordinates.
Compact/Standard/Wide retain those row boundaries and card order, use
12/32/64 pixel gaps, and align rows to the left. They never merge, split or
automatically wrap a manual row; oversized output is rejected by the existing
safety limits. Row IDs exist only in the export cache, so saved layout formats
are unchanged. Margins add 0/24/64/128 pixels per side around the visible-content
plan, and the background covers the whole image.

`RecipeCardRenderer` temporarily reparents one **viewer** widget into a separate
world-space canvas and manually renders a disabled orthographic camera to a
card-sized texture. Parent, sibling, transform and layers are restored in a
`finally` block before yielding. No game order list, prefab or gameplay camera
is modified. Black/white matte renders recover straight RGBA, including a
linear-color-space conversion, instead of trusting the default UI shader's
render-target alpha. The opaque default is the previous export color `#EBE3D1`.

Full-quality RGBA cards are cached in a unique system temporary directory.
`ExportScanlines` performs straight-alpha source-over composition for PNG,
clipboard, full preview and visible detail regions. Only active card rows are
read. PNG uses bounded 64 KiB IDAT chunks; it never allocates a full-board texture
or full compressed-image buffer. Preview work and PNG encoding advance in
small batches on the main thread. File saves complete by renaming a temporary
file; cancellation removes unfinished output and disposes iterators explicitly.

Background, margins and layout changes reuse captures with a short debounce.
A full preview is bounded to 2048 per side and 2 million pixels.
When zoomed, a similarly bounded visible region is regenerated from the same
full-quality cache. Translucent pixels are drawn once against the preview-only
checkerboard. Clipboard publishes PNG, CF_DIBV5 and a white-matte CF_DIB fallback;
paste alpha depends on application support. Copy is bounded to 4096 per side
and 4 million pixels, with an explicit reduced-resolution message.

Safety limits: PNG width 12,000, height 100,000 and 200 million total pixels;
each card at most 4 million pixels and min(8192, GPU max texture size) per side;
raw disk cache at most 512 MiB. Exceeding any applicable limit disables export
with a message. These bounds reduce risk; low-memory systems and unusual custom
card art still need runtime testing. Initial capture takes at least one frame
per card and two camera renders. No output multiplier is exposed or applied.

The existing config file gains `[Image export]`: `Background`, `Color`
(`#RRGGBB`), `Margin` and `Layout`. Defaults: Default / #EBE3D1 / Standard /
Standard. Legacy `Layout=Current` maps to Standard without renumbering the enum.
The legacy `Scale` key stays hidden and is saved as 1; existing 1.5/2
values cannot affect rendering. All export entries use `Browsable=false` tags
and are edited in the export preview; shortcut labels remain visible.
Invalid enum/color values fall back conservatively. Config saves are
debounced. Cancelling the dialog retains the last export settings, while rows,
layout files, viewer zoom and scroll are unchanged. Load hooks, scene loss,
plugin disable, quit and dialog close dispose preview resources. An OS/process
crash can leave this session's temporary cache; automatic cleanup never scans
or deletes unrelated files.

Technical references: [Unity Camera.Render](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Camera.Render.html)
and [Unity blend factors](https://docs.unity3d.com/2018.4/Documentation/Manual/SL-Blend.html).
Configuration visibility follows the optional tags in
[ConfigurationManagerAttributes](https://github.com/BepInEx/BepInEx.ConfigurationManager/blob/master/ConfigurationManagerAttributes.cs)
without adding a runtime dependency on Configuration Manager.

## Interface preferences

The viewer toolbar and export header both open Interface settings. `[Interface]`
`Scale` (0.5–2.0, default 1) and `Theme` (Kitchen / Cream / Ocean / Charcoal)
are hidden in Configuration Manager, saved with a debounce, and independent of
`[Image export] Scale`. Invalid values normalize safely. UI scale uses a screen
fit factor plus the chosen continuous percentage; it never changes board zoom,
card transforms, row membership, layout storage or output pixels.

`UiToolbarLayout` wraps buttons and reserves matching space in the native canvas.
`UiPreviewLayout` keeps image, sidebar, header and actions accessible after scaling.
`RecipeBoard.ApplyInterface` changes only chrome colors/viewport/row handles and
refreshes scroll bounds without invoking row layout. Rounded 24×24 style textures
are recolored in place on theme changes, never rebuilt on scale changes, and freed
with existing plugin-owned textures. The modal blocks board input, supports reset,
Esc and Insert, and flushes preferences on close/scene loss/disable/quit. PNG
backgrounds continue to use export settings exclusively.

## Automated checks and release preparation

```powershell
.\tests\run-tests.ps1 -GameDir '<game-directory>' -PythonPath '<Python-with-Pillow>'
.\package.ps1
```

The C# tests compile production encoder/compositor/layout/clipboard-payload code
with the .NET 3.5 compiler and actual game/BepInEx references. They do not call
Unity native rendering or modify the live clipboard. Python/Pillow independently
checks PNG CRC/zlib/RGBA pixels, preview parity and long-image completion.
Fixtures and test binaries stay under ignored `artifacts` directories.

`package.ps1` verifies DLL versions against `PluginVersion` and writes
SHA256SUMS.txt for the DLL, ready for direct release download. The optional
`-Zip` switch retains local one-DLL archive preparation and writes its checksum;
it refuses to overwrite an existing archive. Neither mode deploys, changes Git
or performs network operations.
See [runtime acceptance](TESTING-v0.9.0.md) and [release notes](RELEASE_NOTES-v0.9.0.md).
With explicit publishing authorization, commit the reviewed source/docs,
tag that commit `v0.9.0`, push, and create a **new** release
using these notes plus `Overcooked2RecipeViewer.dll` and its checksum. Preserve
`v0.8.3` and every existing tag; upload no game assemblies, test fixtures or
intermediate binaries.

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
