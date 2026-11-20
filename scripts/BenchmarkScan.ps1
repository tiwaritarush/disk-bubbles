param(
    [string] $Path = 'C:\',
    [string] $ExecutablePath = (Join-Path $PSScriptRoot '../artifacts/DiskBubbles.exe'),
    [ValidateRange(0, 64)] [int[]] $Workers = @(8, 16, 32, 8),
    [ValidateRange(1, 10)] [int] $Repeats = 1
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$target = (Resolve-Path -LiteralPath $Path).Path
$output = Join-Path $repo ('artifacts/benchmark/run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$runner = Join-Path $output 'ScanBenchmark.exe'
$sdk = (dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^(?<version>\S+) \[(?<path>.+)\]$') {
    throw 'A .NET SDK is required to compile the benchmark.'
}
$compiler = Join-Path $Matches.path "$($Matches.version)/Roslyn/bincore/csc.dll"
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
$references = @('mscorlib.dll', 'System.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
dotnet $compiler /nologo /noconfig /nostdlib+ /target:exe /optimize+ "/out:$runner" $references (Join-Path $repo 'tests/ScanBenchmark.cs')
if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }

Write-Output 'workers,seconds,files,folders,skipped,bytes'
for ($run = 0; $run -lt $Repeats; $run++) {
    foreach ($count in $Workers) {
        & $runner $executable $target $count
        if ($LASTEXITCODE -ne 0) { throw "Scan failed with $count workers." }
    }
}
