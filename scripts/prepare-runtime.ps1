param(
    # Model key from docs/model-and-runtime-manifest.json (e.g. granite-350m or qwen2.5-1.5b).
    # Defaults to the manifest's defaultModel.
    [string]$Model = ''
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifest = Get-Content -LiteralPath (Join-Path $repositoryRoot 'docs/model-and-runtime-manifest.json') -Raw | ConvertFrom-Json

$modelKey = if ($Model) { $Model } else { $manifest.defaultModel }
$modelInfo = $manifest.models.$modelKey
if (-not $modelInfo) {
    $available = ($manifest.models.PSObject.Properties.Name | Sort-Object) -join ', '
    throw "Unknown model '$modelKey'. Available models: $available"
}

$downloads = Join-Path $repositoryRoot 'artifacts/downloads'
$engineDir = Join-Path $repositoryRoot ('artifacts/llama-' + $manifest.engine.version)
$modelDir = Join-Path $repositoryRoot 'models'
New-Item -ItemType Directory -Force -Path $downloads, $engineDir, $modelDir | Out-Null

function Get-VerifiedFile {
    param($item, [string]$destination)
    if (Test-Path -LiteralPath $destination) {
        $existingHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existingHash -ne $item.sha256) { throw "Hash mismatch for existing file: $destination" }
        return
    }

    $partial = $destination + '.partial'
    & curl.exe --fail --location --retry 3 --continue-at - --output $partial $item.url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $($item.url)" }
    $actualHash = (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $item.sha256) { throw "SHA-256 mismatch for $($item.file): $actualHash" }
    Move-Item -LiteralPath $partial -Destination $destination
}

$engineZip = Join-Path $downloads $manifest.engine.file
$modelPath = Join-Path $modelDir $modelInfo.file
Get-VerifiedFile $manifest.engine $engineZip
Get-VerifiedFile $modelInfo $modelPath

$server = Get-ChildItem -LiteralPath $engineDir -Filter 'llama-server.exe' -File -Recurse |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $server) {
    Expand-Archive -LiteralPath $engineZip -DestinationPath $engineDir -Force
    $server = Get-ChildItem -LiteralPath $engineDir -Filter 'llama-server.exe' -File -Recurse |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $server) { throw 'llama-server.exe was not found in the verified archive.' }

$configPath = Join-Path $repositoryRoot 'samples/LightLMChat.ExampleApp/lightlmchat.config.json'
[ordered]@{ serverPath = $server; modelPath = $modelPath } |
    ConvertTo-Json |
    Set-Content -LiteralPath $configPath -Encoding utf8

Write-Output "Model:          $($modelInfo.name) ($modelKey)"
Write-Output "Verified engine: $server"
Write-Output "Verified model:  $modelPath"
Write-Output "Selected in:     $configPath"
Write-Output ''
Write-Output 'Run: dotnet run --project samples/LightLMChat.ExampleApp'
