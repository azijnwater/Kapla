$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$testRoot = Join-Path $projectRoot "Tests"
$output = Join-Path $testRoot "KaplaRegressionTests.exe"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$references = @(
    (Join-Path $framework "System.dll"),
    (Join-Path $framework "System.Core.dll"),
    (Join-Path $framework "System.Runtime.Serialization.dll"),
    (Join-Path $framework "System.Web.Extensions.dll")
    (Join-Path $framework "WPF\WindowsBase.dll")
    (Join-Path $framework "WPF\PresentationCore.dll")
    (Join-Path $framework "WPF\PresentationFramework.dll")
    (Join-Path $framework "System.Xaml.dll")
)
$sources = @(
    (Join-Path $projectRoot "Models.cs"),
    (Join-Path $projectRoot "ThemePalette.cs"),
    (Join-Path $projectRoot "KoboMetadata.cs"),
    (Join-Path $projectRoot "KoboCachedAudiobook.cs"),
    (Join-Path $projectRoot "PlaybackTimeline.cs"),
    (Join-Path $projectRoot "PlaybackProgress.cs"),
    (Join-Path $projectRoot "SleepTimerState.cs"),
    (Join-Path $projectRoot "KoboSyncPolicy.cs"),
    (Join-Path $projectRoot "KoboEndpointPolicy.cs"),
    (Join-Path $projectRoot "AppSettingsStore.cs"),
    (Join-Path $projectRoot "LocalAudiobookMetadata.cs"),
    (Join-Path $testRoot "RegressionTests.cs")
)
$args = @('/nologo', '/target:exe', '/optimize+', "/out:$output")
$args += $references | ForEach-Object { "/reference:$($_)" }
$args += $sources
& $compiler @args
if ($LASTEXITCODE -ne 0) { throw "Regression test build failed with exit code $LASTEXITCODE." }
& $output
if ($LASTEXITCODE -ne 0) { throw "Regression tests failed with exit code $LASTEXITCODE." }
