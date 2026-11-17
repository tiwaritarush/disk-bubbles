param([string]$OutputPath = (Join-Path $PSScriptRoot 'artifacts/DiskBubbles.exe'))

$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $PSScriptRoot 'src/DiskBubbles'
$sdkList = @(dotnet --list-sdks)
if ($sdkList.Count -eq 0 -or $sdkList[-1] -notmatch '^(?<version>\S+) \[(?<path>.+)\]$') {
    throw 'A .NET SDK is required to compile Disk Bubbles.'
}

$compiler = Join-Path $Matches.path "$($Matches.version)/Roslyn/bincore/csc.dll"
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
$references = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xaml.dll', 'System.Windows.Forms.dll',
    'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll'
) | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = @('App.cs', 'Appearance.cs', 'AppInventory.cs', 'AppIcons.cs', 'AppRemoval.cs', 'AppCleanerPage.cs', 'UiMotion.cs', 'MainWindow.cs', 'MainWindow.Layout.cs', 'MainWindow.Recycle.cs', 'DiskScanner.cs',
    'NativeDirectoryReader.cs', 'BubbleMap.cs', 'SystemProtection.cs', 'ShellRecycle.cs', 'RecycleItem.cs',
    'RecycleReview.cs', 'UiVisuals.cs') |
    ForEach-Object { Join-Path $sourceRoot $_ }
$resources = @('Theme.xaml', 'Dark.xaml', 'Light.xaml') | ForEach-Object {
    "/resource:$(Join-Path $sourceRoot $_),DiskBubbles.$_"
}
$resources += "/resource:$(Join-Path $PSScriptRoot 'LICENSE'),DiskBubbles.LICENSE"
$manifest = Join-Path $sourceRoot 'app.manifest'
$icon = Join-Path $sourceRoot 'DiskBubbles.ico'
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null

dotnet $compiler /nologo /noconfig /nostdlib+ /optimize+ /unsafe+ /deterministic+ /target:winexe /platform:anycpu /utf8output `
    "/out:$output" "/win32manifest:$manifest" "/win32icon:$icon" $resources $references $sources
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed (exit $LASTEXITCODE)."
}
Write-Output $output
