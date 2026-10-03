[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$GameDir = $env:OC2_GAME_DIR,
    [string]$PluginDir
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'artifacts\Overcooked2RecipeViewer.dll'
$sourceFile = Get-Item -LiteralPath $source -ErrorAction SilentlyContinue
if ($null -eq $sourceFile -or $sourceFile.PSIsContainer -or $sourceFile.Length -eq 0) {
    throw "Compiled Mod DLL not found or empty: $source. Run build.ps1 successfully first."
}
if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw 'Specify -GameDir with your Overcooked! 2 installation directory, or set OC2_GAME_DIR.'
}
if (-not (Test-Path -LiteralPath $GameDir -PathType Container)) {
    throw "Game directory not found: $GameDir"
}
$GameDir = (Resolve-Path -LiteralPath $GameDir).ProviderPath
$pluginsRoot = Join-Path $GameDir 'BepInEx\plugins'
if (-not (Test-Path -LiteralPath $pluginsRoot -PathType Container)) {
    throw "BepInEx plugins directory not found: $pluginsRoot. Install BepInEx first."
}
$pluginsRoot = (Resolve-Path -LiteralPath $pluginsRoot).ProviderPath.TrimEnd('\', '/')
if ([string]::IsNullOrWhiteSpace($PluginDir)) {
    $PluginDir = Join-Path $pluginsRoot 'Overcooked2RecipeViewer'
}
$targetDirectory = [System.IO.Path]::GetFullPath($PluginDir).TrimEnd('\', '/')
if (-not $targetDirectory.StartsWith($pluginsRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'PluginDir must be a subdirectory of this game installation''s BepInEx\plugins directory.'
}

# Refuse links in the destination chain rather than copying outside the selected game.
$directoryToCheck = $targetDirectory
while ($directoryToCheck.Length -ge $pluginsRoot.Length) {
    if (Test-Path -LiteralPath $directoryToCheck) {
        $directoryItem = Get-Item -LiteralPath $directoryToCheck
        if (-not $directoryItem.PSIsContainer -or
            ($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw "Plugin target path is not a regular directory: $directoryToCheck"
        }
    }
    if ($directoryToCheck -eq $pluginsRoot) { break }
    $directoryToCheck = Split-Path -Parent $directoryToCheck
}
$target = Join-Path $targetDirectory 'Overcooked2RecipeViewer.dll'
$existingTarget = Get-Item -LiteralPath $target -ErrorAction SilentlyContinue
if ($null -ne $existingTarget -and
    ($existingTarget.PSIsContainer -or
     ($existingTarget.Attributes -band [System.IO.FileAttributes]::ReparsePoint))) {
    throw "Mod DLL target is not a regular file: $target"
}

# Both names use the same plugin GUID. Leave old installations untouched and ask
# the user to remove their old copy before installing the renamed DLL.
$legacyDlls = @(Get-ChildItem -LiteralPath $pluginsRoot -Filter 'Overcooked2RecipePreview.dll' -Recurse -File)
if ($legacyDlls.Count -gt 0) {
    throw 'An older Overcooked2RecipePreview.dll is installed. Remove that Mod DLL manually before deploying the renamed DLL; keep its config and layouts.'
}
$otherCopies = @(Get-ChildItem -LiteralPath $pluginsRoot -Filter 'Overcooked2RecipeViewer.dll' -Recurse -File |
    Where-Object { -not $_.FullName.Equals($target, [System.StringComparison]::OrdinalIgnoreCase) })
if ($otherCopies.Count -gt 0) {
    throw 'Overcooked2RecipeViewer.dll is already installed in another plugin folder. Use -PluginDir for that folder or remove the duplicate manually.'
}

if ($PSCmdlet.ShouldProcess($target, 'Copy the compiled Overcooked2RecipeViewer DLL')) {
    if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $targetDirectory | Out-Null
    }
    Copy-Item -LiteralPath $source -Destination $target -Force
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
        throw "Deployed Mod DLL does not match the compiled DLL: $target"
    }
    $deployedFile = Get-Item -LiteralPath $target
    Write-Host "Source: $($sourceFile.FullName)"
    Write-Host "Target: $($deployedFile.FullName)"
    Write-Host "Target modified (local): $($deployedFile.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
}
