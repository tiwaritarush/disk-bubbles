param(
    [string]$Ref = 'HEAD',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/release')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$commit = git -C $repo rev-parse --verify --end-of-options "$Ref^{commit}"
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40,64}$') {
    throw 'Choose a Git commit or release tag to package.'
}

$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$sourceZip = Join-Path $output 'DiskBubbles-source.zip'
$executable = Join-Path $output 'DiskBubbles.exe'
$checksums = Join-Path $output 'SHA256SUMS.txt'
$staging = Join-Path $repo ('artifacts/staging/' + [guid]::NewGuid().ToString('N'))
$snapshot = Join-Path $staging 'source'

git -C $repo archive --format=zip "--output=$sourceZip" $commit
if ($LASTEXITCODE -ne 0) { throw 'Could not archive the selected commit.' }
Expand-Archive -LiteralPath $sourceZip -DestinationPath $snapshot

& (Join-Path $snapshot 'Build.ps1') -OutputPath $executable
& (Join-Path $snapshot 'Verify.ps1') -ExecutablePath $executable -OutputDirectory (Join-Path $staging 'verification')
Get-FileHash -Algorithm SHA256 -LiteralPath $executable, $sourceZip |
    ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content -LiteralPath $checksums -Encoding ascii

Write-Output "Packaged commit $commit"
Get-Item -LiteralPath $executable, $sourceZip, $checksums | Select-Object Name, Length
