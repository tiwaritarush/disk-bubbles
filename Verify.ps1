param(
    [string] $ExecutablePath = (Join-Path $PSScriptRoot 'artifacts/DiskBubbles.exe'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/verification')
)

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$run = Join-Path $OutputDirectory ('run-' + [guid]::NewGuid().ToString('N'))
$run = (New-Item -ItemType Directory -Path $run).FullName
$runner = Join-Path $run 'VerifyDiskBubbles.exe'
$fixture = Join-Path $run 'Storage sample'
New-Item -ItemType Directory -Path $fixture | Out-Null
$outside = (New-Item -ItemType Directory -Path (Join-Path $run 'Outside scan')).FullName
Set-Content -LiteralPath (Join-Path $outside 'outside.txt') -Value 'This file belongs outside the scan.'
try {
    New-Item -ItemType Junction -Path (Join-Path $fixture 'Junction') -Target $outside | Out-Null
} catch {
    Write-Output 'This environment could not create a junction. The junction case was skipped.'
}

$sdk = (dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^(?<version>\S+) \[(?<path>.+)\]$') {
    throw 'A .NET SDK is required to compile the verification runner.'
}

$compiler = Join-Path $Matches.path "$($Matches.version)/Roslyn/bincore/csc.dll"
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'WPF/WindowsBase.dll',
    'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$source = Join-Path $PSScriptRoot 'tests/Verification.cs'
dotnet $compiler /nologo /noconfig /nostdlib+ /target:exe /optimize+ /deterministic+ "/out:$runner" $references $source
if ($LASTEXITCODE -ne 0) { throw "Verification build failed (exit $LASTEXITCODE)." }

& $runner $executable $fixture
if ($LASTEXITCODE -ne 0) { throw "Verification failed (exit $LASTEXITCODE)." }
Write-Output "Verification artifacts saved to $run"
