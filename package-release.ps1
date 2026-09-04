$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = (Get-Content (Join-Path $projectRoot "VERSION") -Raw).Trim()
$releaseDirectory = Join-Path $projectRoot "release"
$stageDirectory = Join-Path $releaseDirectory "Kapla-$version-windows-x64-portable"
$zipPath = Join-Path $releaseDirectory "Kapla-$version-windows-x64-portable.zip"
$checksumPath = Join-Path $releaseDirectory "Kapla-$version-checksums.txt"

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "VERSION must contain a semantic version such as 0.1.0."
}

& (Join-Path $projectRoot "Tests\run-tests.ps1")
& (Join-Path $projectRoot "build.ps1")

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
if (Test-Path $stageDirectory) { Remove-Item -LiteralPath $stageDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $stageDirectory | Out-Null
Copy-Item (Join-Path $projectRoot "outputs\Kapla.exe") $stageDirectory
Copy-Item (Join-Path $projectRoot "outputs\Launch-Kapla.cmd") $stageDirectory
Copy-Item (Join-Path $projectRoot "outputs\README.md") $stageDirectory
Copy-Item (Join-Path $projectRoot "outputs\Assets") $stageDirectory -Recurse
Copy-Item (Join-Path $projectRoot "outputs\docs") $stageDirectory -Recurse
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $stageDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Encoding ascii -Value "$hash  $(Split-Path -Leaf $zipPath)"
Write-Output $zipPath
Write-Output $checksumPath
