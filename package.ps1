# Local package preparation only. Never deploys, tags, pushes or publishes.
$ErrorActionPreference = 'Stop'
$taskArtifactDirectory = Join-Path $PSScriptRoot 'artifacts'
$taskDll = Join-Path $taskArtifactDirectory 'Overcooked2RecipeViewer.dll'
if (-not (Test-Path -LiteralPath $taskDll -PathType Leaf)) { throw 'Run build.ps1 successfully first.' }
$taskPluginSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'RecipeViewerPlugin.cs'))
$taskVersionMatch = [regex]::Match($taskPluginSource, 'PluginVersion = "([0-9]+\.[0-9]+\.[0-9]+)"')
if (-not $taskVersionMatch.Success) { throw 'Could not read PluginVersion.' }
$taskVersion = $taskVersionMatch.Groups[1].Value
$taskAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($taskDll).Version.ToString()
$taskFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($taskDll)
if ($taskAssemblyVersion -ne ($taskVersion + '.0') -or $taskFileVersion.FileVersion -ne ($taskVersion + '.0') -or $taskFileVersion.ProductVersion -ne $taskVersion) {
    throw 'DLL and source versions differ. Rebuild before packaging.'
}
$taskZipName = "Overcooked2RecipeViewer-v$taskVersion.zip"
$taskZipPath = Join-Path $taskArtifactDirectory $taskZipName
if (Test-Path -LiteralPath $taskZipPath) { throw "Package already exists; preserve/move it before packaging again: $taskZipPath" }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskCreated = $false
try {
    $taskZipStream = [IO.File]::Open($taskZipPath, [IO.FileMode]::CreateNew)
    $taskCreated = $true
    try {
        $taskArchive = New-Object IO.Compression.ZipArchive($taskZipStream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $taskEntry = $taskArchive.CreateEntry('Overcooked2RecipeViewer.dll', [IO.Compression.CompressionLevel]::Optimal)
            $taskEntryStream = $taskEntry.Open()
            try {
                $taskDllStream = [IO.File]::OpenRead($taskDll)
                try { $taskDllStream.CopyTo($taskEntryStream) } finally { $taskDllStream.Dispose() }
            } finally { $taskEntryStream.Dispose() }
        } finally { $taskArchive.Dispose() }
    } finally { $taskZipStream.Dispose() }
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskZipPath)
    try {
        if ($taskArchive.Entries.Count -ne 1 -or $taskArchive.Entries[0].FullName -ne 'Overcooked2RecipeViewer.dll') { throw 'Unexpected ZIP contents.' }
        $taskEntryStream = $taskArchive.Entries[0].Open()
        $taskHasher = [Security.Cryptography.SHA256]::Create()
        try { $taskEntryHash = [BitConverter]::ToString($taskHasher.ComputeHash($taskEntryStream)).Replace('-', '') }
        finally { $taskHasher.Dispose(); $taskEntryStream.Dispose() }
        if ($taskEntryHash -ne (Get-FileHash -LiteralPath $taskDll -Algorithm SHA256).Hash) { throw 'ZIP/DLL checksum mismatch.' }
    } finally { $taskArchive.Dispose() }
    $taskZipHash = (Get-FileHash -LiteralPath $taskZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $taskArtifactDirectory 'SHA256SUMS.txt'), "$taskZipHash  $taskZipName`n", (New-Object Text.UTF8Encoding($false)))
} catch {
    if ($taskCreated -and (Test-Path -LiteralPath $taskZipPath)) { Remove-Item -LiteralPath $taskZipPath }
    throw
}
Write-Host "Prepared: $taskZipPath"
Write-Host "SHA256: $taskZipHash"
