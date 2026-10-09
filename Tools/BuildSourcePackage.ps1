param()
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
Set-Location -LiteralPath $projectRoot
[xml]$project=Get-Content Source/SpectrumKlinePlayer.csproj -Raw -Encoding UTF8
$version=[string](@($project.Project.PropertyGroup.Version | Where-Object { $_ })[0])
if($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){throw 'Invalid version'}
$release=Join-Path $projectRoot 'Distribution'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$target=Join-Path $release "MusicTide-Source-v$version.zip"
$pending=Join-Path $release 'MusicTide-Source-building.zip'
$files=@('README.md','LICENSE','THIRD_PARTY_NOTICES.md','CONTRIBUTING.md','CHANGELOG.md','.gitignore',
    'Distribution/README.md','Docs/README.md','Docs/UserGuide.md','Docs/Development.md','Docs/Privacy.md',
    'Docs/USBSideScreenPlan.md','Tools/README.md','Tools/Installer/model.json')
$files+=Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Docs') -File -Filter '*.md' | ForEach-Object { 'Docs/'+$_.Name }
foreach($folder in @('Source','Tools','Licenses')) {
    $files+=Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|Sandbox|OfflineSandbox|Sysinternals)\\' -and
            ($_.Extension -in @('.cs','.csproj','.ps1','.py','.manifest') -or
                ($folder -eq 'Source' -and $_.Extension -in @('.ico','.png','.md')) -or
                ($folder -eq 'Licenses' -and $_.Extension -in @('.txt','.md'))) } |
        ForEach-Object { $_.FullName.Substring($projectRoot.Length+1).Replace('\','/') }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
if(Test-Path -LiteralPath $pending){Remove-Item -LiteralPath $pending}
try {
    $zip=[IO.Compression.ZipFile]::Open($pending,[IO.Compression.ZipArchiveMode]::Create)
    foreach($file in ($files | Sort-Object -Unique)) {
        $path=Join-Path $projectRoot $file
        if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing source file: $file"}
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$path,$file,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { if($zip){$zip.Dispose()} }
Move-Item -LiteralPath $pending -Destination $target -Force
Get-ChildItem -LiteralPath $release -File | Where-Object {
    $_.Name -match '^MusicTide-Source-v\d+\.\d+\.\d+(\.\d+)?\.zip$' -and $_.FullName -ne $target
} | ForEach-Object {
    if([IO.Path]::GetDirectoryName($_.FullName) -ne $release){throw 'Cleanup escaped release directory'}
    Remove-Item -LiteralPath $_.FullName
}
Get-FileHash -LiteralPath $target | Select-Object Algorithm,Hash,Path | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $release 'Source-SHA256.json') -Encoding UTF8
Write-Output $target
