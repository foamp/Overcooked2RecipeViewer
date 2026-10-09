param(
    [string]$GameDir = $env:OC2_GAME_DIR,
    [string]$CompilerPath = (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v3.5\csc.exe'),
    [string]$PythonPath
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GameDir)) { throw 'Specify -GameDir or OC2_GAME_DIR.' }
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRepo ('artifacts\tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskOutput | Out-Null
$taskManaged = Join-Path $GameDir 'Overcooked2_Data\Managed'
$taskCore = Join-Path $GameDir 'BepInEx\core'
$taskExe = Join-Path $taskOutput 'ExportTests.exe'
$taskArguments = @('/nologo','/codepage:65001','/noconfig','/nostdlib+','/target:exe',"/out:$taskExe")
foreach ($taskReference in @((Join-Path $taskManaged 'mscorlib.dll'),(Join-Path $taskManaged 'System.dll'),(Join-Path $taskManaged 'System.Core.dll'),(Join-Path $taskCore 'BepInEx.dll'))) {
    $taskArguments += "/reference:$taskReference"
}
foreach ($taskSource in @('RecipeExport.cs','RecipeUiSettings.cs','PngStreamWriter.cs','ClipboardImage.cs','RecipeLayoutStore.cs','tests\ExportTests.cs')) {
    $taskArguments += (Join-Path $taskRepo $taskSource)
}
& $CompilerPath $taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $taskExe $taskOutput $taskCore
if ($LASTEXITCODE -ne 0) { throw 'C# tests failed.' }
if ($PythonPath) {
    & $PythonPath (Join-Path $PSScriptRoot 'verify_png.py') $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Independent PNG decoding failed.' }
}
Write-Host "Test output: $taskOutput"
