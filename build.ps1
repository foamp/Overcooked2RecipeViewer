param(
    [string]$GameDir = 'E:\game\steam\steamapps\common\Overcooked! 2'
)

$ErrorActionPreference = 'Stop'

$compiler = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
$source = Join-Path $PSScriptRoot 'RecipePreviewPlugin.cs'
$nativeSource = Join-Path $PSScriptRoot 'RecipeBoard.cs'
$clipboardSource = Join-Path $PSScriptRoot 'ClipboardImage.cs'
$pngSource = Join-Path $PSScriptRoot 'PngStreamWriter.cs'
$output = Join-Path $PSScriptRoot 'Overcooked2RecipePreview.dll'
$managed = Join-Path $GameDir 'Overcooked2_Data\Managed'
$core = Join-Path $GameDir 'BepInEx\core'

$references = @(
    (Join-Path $core 'BepInEx.dll'),
    (Join-Path $core '0Harmony.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll'),
    (Join-Path $managed 'UnityEngine.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $managed 'UnityEngine.AnimationModule.dll'),
    (Join-Path $managed 'UnityEngine.ImageConversionModule.dll'),
    (Join-Path $managed 'UnityEngine.IMGUIModule.dll'),
    (Join-Path $managed 'UnityEngine.UIModule.dll'),
    (Join-Path $managed 'UnityEngine.UI.dll')
)

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "Required reference not found: $reference"
    }
}

$arguments = @(
    '/nologo',
    '/target:library',
    '/optimize+',
    "/out:$output"
)

foreach ($reference in $references) {
    $arguments += "/reference:$reference"
}

$arguments += $source
$arguments += $nativeSource
$arguments += $clipboardSource
$arguments += $pngSource

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Built: $output"
