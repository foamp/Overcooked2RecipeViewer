param(
    [string]$GameDir = $env:OC2_GAME_DIR,
    [string]$CompilerPath = (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v3.5\csc.exe')
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw 'Specify -GameDir with your Overcooked! 2 installation directory, or set OC2_GAME_DIR.'
}
if (-not (Test-Path -LiteralPath $GameDir -PathType Container)) {
    throw "Game directory not found: $GameDir"
}
$GameDir = (Resolve-Path -LiteralPath $GameDir).ProviderPath
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw "C# compiler not found: $CompilerPath. Install the .NET Framework 3.5 tools or specify -CompilerPath."
}

$sources = @(
    'AssemblyInfo.cs',
    'RecipeViewerPlugin.cs',
    'RecipeBoard.cs',
    'RecipeExport.cs',
    'RecipeCardRenderer.cs',
    'RecipeExportPreview.cs',
    'ClipboardImage.cs',
    'PngStreamWriter.cs',
    'RecipeSortMetadata.cs',
    'RecipeLayoutStore.cs',
    'RecipeUiText.cs',
    'RecipeUiSettings.cs',
    'RecipeUiAppearance.cs'
) | ForEach-Object { Join-Path $PSScriptRoot $_ }
$outputDirectory = Join-Path $PSScriptRoot 'artifacts'
$output = Join-Path $outputDirectory 'Overcooked2RecipeViewer.dll'
$managed = Join-Path $GameDir 'Overcooked2_Data\Managed'
$core = Join-Path $GameDir 'BepInEx\core'

$references = @(
    (Join-Path $managed 'mscorlib.dll'),
    (Join-Path $managed 'System.dll'),
    (Join-Path $managed 'System.Core.dll'),
    (Join-Path $core 'BepInEx.dll'),
    (Join-Path $core '0Harmony.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll'),
    (Join-Path $managed 'UnityEngine.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $managed 'UnityEngine.AnimationModule.dll'),
    (Join-Path $managed 'UnityEngine.ImageConversionModule.dll'),
    (Join-Path $managed 'UnityEngine.IMGUIModule.dll'),
    (Join-Path $managed 'UnityEngine.TextRenderingModule.dll'),
    (Join-Path $managed 'UnityEngine.UIModule.dll'),
    (Join-Path $managed 'UnityEngine.UI.dll')
)

foreach ($file in ($sources + $references)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required source or reference not found: $file"
    }
}
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$arguments = @(
    '/nologo',
    '/codepage:65001',
    '/noconfig',
    '/nostdlib+',
    '/target:library',
    '/optimize+',
    "/out:$output"
)
foreach ($reference in $references) {
    $arguments += "/reference:$reference"
}
$arguments += $sources

& $CompilerPath $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}
Write-Host "Built: $output"
