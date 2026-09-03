$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDirectory = Join-Path $projectRoot "outputs"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$frameworkDirectory = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$wpfDirectory = Join-Path $frameworkDirectory "WPF"

if (-not (Test-Path $compiler)) {
    throw "The .NET Framework C# compiler was not found at $compiler."
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputFile = Join-Path $outputDirectory "Kapla.exe"
$sources = Get-ChildItem -Path $projectRoot -Filter "*.cs" | ForEach-Object { $_.FullName }
$references = @(
    (Join-Path $frameworkDirectory "System.dll"),
    (Join-Path $frameworkDirectory "System.Core.dll"),
    (Join-Path $frameworkDirectory "System.Data.dll"),
    (Join-Path $frameworkDirectory "System.Runtime.Serialization.dll"),
    (Join-Path $frameworkDirectory "System.Net.Http.dll"),
    (Join-Path $frameworkDirectory "System.Security.dll"),
    (Join-Path $frameworkDirectory "System.Web.dll"),
    (Join-Path $frameworkDirectory "System.Web.Extensions.dll"),
    (Join-Path $frameworkDirectory "System.Xml.dll"),
    (Join-Path $wpfDirectory "WindowsBase.dll"),
    (Join-Path $wpfDirectory "PresentationCore.dll"),
    (Join-Path $wpfDirectory "PresentationFramework.dll"),
    (Join-Path $frameworkDirectory "System.Xaml.dll")
)

$referenceArguments = $references | ForEach-Object { "/reference:$($_)" }
& $compiler /nologo /target:winexe /optimize+ "/out:$outputFile" $referenceArguments $sources
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Copy-Item (Join-Path $projectRoot "README.md") $outputDirectory -Force
Copy-Item (Join-Path $projectRoot "Launch-Kapla.cmd") $outputDirectory -Force
Copy-Item (Join-Path $projectRoot "Assets") $outputDirectory -Recurse -Force
Write-Output "Built $outputFile"
