$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'Overcooked2RecipePreview.dll'
$targetDirectory = 'E:\game\steam\steamapps\common\Overcooked! 2\BepInEx\plugins\zhimo'
$target = Join-Path $targetDirectory 'Overcooked2RecipePreview.dll'

$sourceFile = Get-Item -LiteralPath $source -ErrorAction SilentlyContinue
if ($null -eq $sourceFile -or $sourceFile.PSIsContainer) {
    throw "Compiled Mod DLL not found: $source"
}
if ($sourceFile.Length -eq 0) {
    throw "Compiled Mod DLL is empty: $source"
}

$targetParent = Split-Path -Parent $targetDirectory
if (-not (Test-Path -LiteralPath $targetParent -PathType Container)) {
    throw "Plugin parent directory not found: $targetParent"
}
if (-not (Test-Path -LiteralPath $targetDirectory)) {
    New-Item -ItemType Directory -Path $targetDirectory | Out-Null
}
$directoryItem = Get-Item -LiteralPath $targetDirectory
if (-not $directoryItem.PSIsContainer -or
    ($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
    throw "Plugin target directory is not a regular directory: $targetDirectory"
}

$existingTarget = Get-Item -LiteralPath $target -ErrorAction SilentlyContinue
if ($null -ne $existingTarget -and
    ($existingTarget.PSIsContainer -or
     ($existingTarget.Attributes -band [System.IO.FileAttributes]::ReparsePoint))) {
    throw "Mod DLL target is not a regular file: $target"
}

Copy-Item -LiteralPath $source -Destination $target -Force

$deployedFile = Get-Item -LiteralPath $target
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
    throw "Deployed Mod DLL does not match the compiled DLL: $target"
}

Write-Host "Source: $($sourceFile.FullName)"
Write-Host "Target: $($deployedFile.FullName)"
Write-Host "Target modified (local): $($deployedFile.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
