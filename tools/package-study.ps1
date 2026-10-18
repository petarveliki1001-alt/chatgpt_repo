$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$name='CpuStudy_v1'
$stage=Join-Path $root "artifacts/study/$name"
$out=Join-Path $root "downloads/$name.zip"
if(Test-Path $out) { throw 'This source archive already exists; do not overwrite a delivery.' }
if(Test-Path $stage) { Remove-Item $stage -Force -Recurse }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item CpuStudy.sln $stage
Copy-Item verification/CPU-STUDY.md "$stage/README.md"
foreach($folder in @('NativeSpeech','NativeSpeech.Benchmarks','experiments/QuantizeOnnx','experiments/PocketProbe','verification/results/cpu-study/linux')) {
    foreach($file in Get-ChildItem $folder -File -Recurse) {
        $relative=[IO.Path]::GetRelativePath($root,$file.FullName)
        if($relative -match '(^|[/\\])(bin|obj)([/\\])') { continue }
        if($file.Extension -notin @('.cs','.csproj','.json','.proto','.txt') -and $file.Name -ne 'LICENSE') { continue }
        $target=Join-Path $stage $relative
        New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
        Copy-Item $file.FullName $target
    }
}
New-Item -ItemType Directory -Force (Split-Path $out -Parent) | Out-Null
Compress-Archive -Path $stage -DestinationPath $out -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($out)
try {
    if($zip.Entries | Where-Object { $_.FullName -match '(?i)\.(exe|dll|lib|pdb|py|onnx|ort)$|/(bin|obj)/' }) { throw 'Binary/model file found in source-only archive.' }
    if($zip.Entries | Where-Object { !$_.FullName.Replace('\','/').StartsWith("$name/") }) { throw 'Unexpected archive root.' }
} finally { $zip.Dispose() }
Write-Host "Source only: $out"
