param([string]$Published = 'artifacts/app')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
[xml]$project = Get-Content 'PersonalMemoirsReader/PersonalMemoirsReader.csproj'
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
$name = "MemoirsReader_v$version"
$out = Join-Path $root "downloads/$name.zip"
if (Test-Path $out) { throw "Already delivered: $name. Increment the version before changing a delivery." }
if (!(Test-Path "$Published/MemoirsReader.exe")) { throw 'Publish the Windows x64 app first.' }
$bad = Get-ChildItem $Published -Recurse -File | Where-Object { $_.Name -match '(?i)python|whisper|\.py$' }
if ($bad) { throw 'Unexpected Python/Whisper file in published app.' }
$stage = Join-Path $root "artifacts/package/$name"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Get-ChildItem $Published -Exclude '*.pdb' | Copy-Item -Destination $stage -Recurse
Copy-Item MemoirsReader.sln,README.md -Destination $stage
foreach ($directory in @('PersonalMemoirsReader','NativeSpeech','NativeSpeech.Tests','tools')) {
    $destination = Join-Path $stage $directory
    New-Item -ItemType Directory -Force $destination | Out-Null
    Get-ChildItem $directory -File | Where-Object { $_.Extension -in @('.cs','.csproj','.json','.ps1') } | Copy-Item -Destination $destination
}
Copy-Item NativeSpeech/licenses -Destination "$stage/NativeSpeech" -Recurse
New-Item -ItemType Directory -Force (Split-Path $out -Parent) | Out-Null
Compress-Archive -Path $stage -DestinationPath $out -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($out)
try {
    if ($zip.Entries | Where-Object { !$_.FullName.Replace('\','/').StartsWith("$name/") }) { throw 'Unversioned ZIP entry.' }
    if ($zip.Entries | Where-Object { $_.FullName -match '(?i)python|whisper|\.py$' }) { throw 'Unexpected Python/Whisper archive entry.' }
} finally { $zip.Dispose() }
Write-Host "Created $out"
