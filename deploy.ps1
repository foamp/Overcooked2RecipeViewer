[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$GameDir = $env:OC2_GAME_DIR,
    [string]$PluginDir,
    [switch]$ReplaceLegacy
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

# Both names use the same plugin GUID. Migration must be explicitly selected and
# may only replace this Mod's single legacy copy in the chosen plugin directory.
$legacyTarget = $null
$legacyDlls = @(Get-ChildItem -LiteralPath $pluginsRoot -Filter 'Overcooked2RecipePreview.dll' -Recurse -File)
if ($legacyDlls.Count -gt 0) {
    $expectedLegacy = Join-Path $targetDirectory 'Overcooked2RecipePreview.dll'
    if (-not $ReplaceLegacy -or $legacyDlls.Count -ne 1 -or
        -not $legacyDlls[0].FullName.Equals($expectedLegacy, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'An older Overcooked2RecipePreview.dll is installed. Remove it manually, or use -ReplaceLegacy with -PluginDir pointing to its folder; keep config and layouts.'
    }
    if ($null -ne $existingTarget -or
        ($legacyDlls[0].Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw 'Legacy replacement requires one regular legacy DLL and no existing Viewer DLL in the selected folder.'
    }
    $legacyTarget = $expectedLegacy
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
    if ($null -eq $legacyTarget) {
        Copy-Item -LiteralPath $source -Destination $target -Force
    } else {
        $backupName = 'Overcooked2RecipeViewer-' + [Guid]::NewGuid().ToString('N') + '.dll.bak'
        $backup = Join-Path ([System.IO.Path]::GetTempPath()) $backupName
        Copy-Item -LiteralPath $legacyTarget -Destination $backup
        try {
            # Update and then rename the same DLL: never leave two active copies.
            Copy-Item -LiteralPath $source -Destination $legacyTarget -Force
            if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath $legacyTarget -Algorithm SHA256).Hash) {
                throw 'Legacy replacement DLL does not match the compiled DLL.'
            }
            Move-Item -LiteralPath $legacyTarget -Destination $target
        } catch {
            $deploymentError = $_
            try {
                if ((Test-Path -LiteralPath $target -PathType Leaf) -and
                    -not (Test-Path -LiteralPath $legacyTarget)) {
                    Move-Item -LiteralPath $target -Destination $legacyTarget
                }
                Copy-Item -LiteralPath $backup -Destination $legacyTarget -Force
            } catch {
                throw "Deployment rollback failed; original DLL backup retained at: $backup"
            }
            Remove-Item -LiteralPath $backup
            throw $deploymentError
        }
        Remove-Item -LiteralPath $backup
    }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
        throw "Deployed Mod DLL does not match the compiled DLL: $target"
    }
    $deployedFile = Get-Item -LiteralPath $target
    Write-Host "Source: $($sourceFile.FullName)"
    Write-Host "Target: $($deployedFile.FullName)"
    Write-Host "Target modified (local): $($deployedFile.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
}
