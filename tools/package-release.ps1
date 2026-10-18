$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
[xml]$project = Get-Content 'PersonalMemoirsReader/PersonalMemoirsReader.csproj'
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
$name = "MemoirsReader_v$version"
$out = Join-Path $root "downloads/$name.zip"
if (Test-Path $out) { throw "Already delivered: $name. Increment the version before changing a delivery." }
$stage = Join-Path $root "artifacts/package/$name"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item MemoirsReader.sln,README.md -Destination $stage
foreach ($directory in @('PersonalMemoirsReader','NativeSpeech','NativeSpeech.Tests','PocketWorker')) {
    $destination = Join-Path $stage $directory
    New-Item -ItemType Directory -Force $destination | Out-Null
    Get-ChildItem $directory -File | Where-Object { $_.Extension -in @('.cs','.csproj','.json') } | Copy-Item -Destination $destination
}
Copy-Item NativeSpeech/licenses -Destination "$stage/NativeSpeech" -Recurse
New-Item -ItemType Directory -Force "$stage/tools" | Out-Null
Copy-Item tools/package-release.ps1 "$stage/tools/"
New-Item -ItemType Directory -Force (Split-Path $out -Parent) | Out-Null
Compress-Archive -Path $stage -DestinationPath $out -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($out)
try {
    if ($zip.Entries | Where-Object { !$_.FullName.Replace('\','/').StartsWith("$name/") }) { throw 'Unversioned ZIP entry.' }
    if ($zip.Entries | Where-Object { $_.FullName -match '(?i)\.(exe|dll|lib|pdb|py|onnx|ort)$|/(bin|obj)/' }) { throw 'Unexpected binary/model file in source-only ZIP.' }
    if (!($zip.Entries | Where-Object { $_.FullName.Replace('\','/') -eq "$name/PersonalMemoirsReader/MainForm.Designer.cs" })) { throw 'Form Designer source missing.' }
} finally { $zip.Dispose() }
Write-Host "Source only: $out"
