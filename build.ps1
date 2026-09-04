$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDirectory = Join-Path $projectRoot "outputs"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$frameworkDirectory = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$wpfDirectory = Join-Path $frameworkDirectory "WPF"
$winMetadataDirectory = Join-Path $env:WINDIR "System32\WinMetadata"
$version = (Get-Content (Join-Path $projectRoot "VERSION") -Raw).Trim()

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "VERSION must contain a semantic version such as 0.1.0."
}

if (-not (Test-Path $compiler)) {
    throw "The .NET Framework C# compiler was not found at $compiler."
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputFile = Join-Path $outputDirectory "Kapla.exe"
$sources = Get-ChildItem -Path $projectRoot -Filter "*.cs" | ForEach-Object { $_.FullName }
$generatedVersionSource = Join-Path ([IO.Path]::GetTempPath()) ("Kapla-AssemblyInfo-" + [Guid]::NewGuid().ToString("N") + ".cs")
[IO.File]::WriteAllText($generatedVersionSource, @"
using System.Reflection;
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
[assembly: AssemblyInformationalVersion("$version")]
"@)
$sources += $generatedVersionSource
$references = @(
    (Join-Path $frameworkDirectory "System.dll"),
    (Join-Path $frameworkDirectory "System.Core.dll"),
    (Join-Path $frameworkDirectory "System.Data.dll"),
    (Join-Path $frameworkDirectory "System.Runtime.Serialization.dll"),
    (Join-Path $frameworkDirectory "System.Net.Http.dll"),
    (Join-Path $frameworkDirectory "System.Security.dll"),
    (Join-Path $frameworkDirectory "System.Runtime.dll"),
    (Join-Path $frameworkDirectory "System.Web.dll"),
    (Join-Path $frameworkDirectory "System.Web.Extensions.dll"),
    (Join-Path $frameworkDirectory "System.Xml.dll"),
    (Join-Path $frameworkDirectory "System.Runtime.InteropServices.WindowsRuntime.dll"),
    (Join-Path $frameworkDirectory "System.Runtime.WindowsRuntime.dll"),
    (Join-Path $winMetadataDirectory "Windows.Foundation.winmd"),
    (Join-Path $winMetadataDirectory "Windows.Media.winmd"),
    (Join-Path $wpfDirectory "WindowsBase.dll"),
    (Join-Path $wpfDirectory "PresentationCore.dll"),
    (Join-Path $wpfDirectory "PresentationFramework.dll"),
    (Join-Path $frameworkDirectory "System.Xaml.dll")
)

$referenceArguments = $references | ForEach-Object { "/reference:$($_)" }
try {
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$outputFile" $referenceArguments $sources
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $generatedVersionSource -Force -ErrorAction SilentlyContinue
}

Copy-Item (Join-Path $projectRoot "README.md") $outputDirectory -Force
Copy-Item (Join-Path $projectRoot "Launch-Kapla.cmd") $outputDirectory -Force
$outputAssets = Join-Path $outputDirectory "Assets"
if (Test-Path $outputAssets) { Remove-Item -LiteralPath $outputAssets -Recurse -Force }
Copy-Item (Join-Path $projectRoot "Assets") $outputDirectory -Recurse -Force
$outputDocs = Join-Path $outputDirectory "docs"
if (Test-Path $outputDocs) { Remove-Item -LiteralPath $outputDocs -Recurse -Force }
Copy-Item (Join-Path $projectRoot "docs") $outputDirectory -Recurse -Force
Write-Output "Built $outputFile"
