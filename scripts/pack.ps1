<#
.SYNOPSIS
    Assembles the offline LightLMChat distribution bundle into dist/.

.DESCRIPTION
    1. Publishes the example app (Release).
    2. Locates the pinned engine and model already downloaded by scripts/prepare-runtime.ps1.
    3. Re-verifies their SHA-256 hashes against docs/model-and-runtime-manifest.json.
    4. Copies the app, engine, model, licenses, and manifest into a single dist/ folder.
    5. Optionally zips dist/ for easy copying to an offline Windows x64 PC.

.PARAMETER Zip
    Also create LightLMChat-ExampleApp.zip next to dist/.

.PARAMETER SelfContained
    Publish with the .NET runtime bundled (larger, but runs without a pre-installed .NET).

.PARAMETER Model
    Model key to bundle (e.g. granite-350m or qwen2.5-1.5b). Defaults to the manifest default.

.EXAMPLE
    .\scripts\prepare-runtime.ps1   # one-time: download + verify engine and model
    .\scripts\pack.ps1 -Zip
#>
param(
    [switch]$Zip,
    [switch]$SelfContained,
    [string]$Model = ''
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $root 'docs/model-and-runtime-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$modelKey = if ($Model) { $Model } else { $manifest.defaultModel }
$selectedModel = $manifest.models.$modelKey
if (-not $selectedModel) {
    $available = ($manifest.models.PSObject.Properties.Name | Sort-Object) -join ', '
    throw "Unknown model '$modelKey'. Available models: $available"
}

# 1. Publish the example app.
Write-Host 'Publishing the example app (Release)...'
$csproj = Join-Path $root 'samples/LightLMChat.ExampleApp/LightLMChat.ExampleApp.csproj'
$publishArgs = @('-c', 'Release', '--nologo', '-v', 'minimal')
if ($SelfContained) { $publishArgs += @('-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false') }
dotnet publish $csproj @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$publishDir = if ($SelfContained) {
    Join-Path $root 'samples/LightLMChat.ExampleApp/bin/Release/net10.0-windows/win-x64/publish'
} else {
    Join-Path $root 'samples/LightLMChat.ExampleApp/bin/Release/net10.0-windows/publish'
}
if (-not (Test-Path -LiteralPath $publishDir)) { throw "Publish output not found: $publishDir" }

# 2. Locate the engine (and its companion DLLs) and the model.
$engineRoot = Join-Path $root ('artifacts/llama-' + $manifest.engine.version)
$engineExe = Get-ChildItem -LiteralPath $engineRoot -Filter 'llama-server.exe' -File -Recurse |
    Select-Object -First 1
$modelPath = Join-Path $root ('models/' + $selectedModel.file)
if (-not $engineExe) { throw "Engine not found in $engineRoot. Run scripts/prepare-runtime.ps1 first." }
if (-not (Test-Path -LiteralPath $modelPath)) { throw "Model not found: $modelPath. Run scripts/prepare-runtime.ps1 first." }

# 3. Verify hashes (the engine hash is for its zip archive; the model hash is for the .gguf).
function Assert-Hash([string]$Path, [string]$Expected, [string]$Label) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) { throw "$Label SHA-256 mismatch for $Path`nExpected $Expected`nActual   $actual" }
}
$engineZip = Join-Path $root ('artifacts/downloads/' + $manifest.engine.file)
if (Test-Path -LiteralPath $engineZip) { Assert-Hash $engineZip $manifest.engine.sha256 'Engine archive' }
else { Write-Warning 'Engine archive not found; skipping its re-verification (prepare-runtime.ps1 verified it during download).' }
Assert-Hash $modelPath $selectedModel.sha256 'Model'

# 4. Assemble dist/.
$dist = Join-Path $root 'dist'
if (Test-Path -LiteralPath $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
$distRuntime = Join-Path $dist 'runtime'
$distLicenses = Join-Path $dist 'licenses'
New-Item -ItemType Directory -Force -Path $distRuntime, $distLicenses | Out-Null

Write-Host 'Copying app files...'
Copy-Item -Path (Join-Path $publishDir '*') -Destination $dist -Recurse -Force

Write-Host 'Copying engine and model into runtime/...'
Copy-Item -Path (Join-Path $engineExe.DirectoryName '*') -Destination $distRuntime -Recurse -Force
Copy-Item -LiteralPath $modelPath -Destination (Join-Path $distRuntime $selectedModel.file)

Write-Host 'Copying licenses and manifest...'
Copy-Item -LiteralPath (Join-Path $root 'packaging/licenses/Apache-2.0.txt') -Destination $distLicenses
Copy-Item -LiteralPath (Join-Path $root 'packaging/licenses/MIT-llama.cpp.txt') -Destination $distLicenses
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $distLicenses 'MIT-LightLMChat.txt')
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $dist 'model-and-runtime-manifest.json')

Write-Host "Bundle ready: $dist"

# 5. Optional zip.
if ($Zip) {
    $zipPath = Join-Path $root 'LightLMChat-ExampleApp.zip'
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Zipped bundle: $zipPath"
}
