param([switch]$SkipPublish)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location -LiteralPath $projectRoot
[xml]$projectXml=Get-Content -LiteralPath 'Source/SpectrumKlinePlayer.csproj' -Raw -Encoding UTF8
$version=[string](@($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0])
if($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){throw 'Invalid release version'}
$releaseRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'Distribution'))
$setupPath=Join-Path $releaseRoot ("MusicTideSetup-v$version.exe")
$pendingPath=Join-Path $releaseRoot 'MusicTideSetup-building.exe'
New-Item -ItemType Directory -Path $releaseRoot,(Join-Path $projectRoot 'Build/Installer') -Force | Out-Null
if(!(Test-Path -LiteralPath 'Build/Installer/RuntimePayload/Runtime/DotNet/dotnet.exe')){throw 'Missing private .NET runtime; see Docs/Development.md'}
if(!(Test-Path -LiteralPath 'Runtime/Ollama/ollama.exe')){throw 'Missing Ollama runtime; see Docs/Development.md'}
if(!$SkipPublish){
    dotnet publish Source/SpectrumKlinePlayer.csproj -c Release --self-contained false -p:AppHostDotNetSearch=AppRelative '-p:AppHostRelativeDotNet=..\Runtime\DotNet' -o Build/Installer/AppPayload
    if($LASTEXITCODE -ne 0){throw '主程序发布失败'}
}
$payload=Join-Path $projectRoot 'Build\Installer\RuntimePayload'
$appPayload=Join-Path $projectRoot 'Build/Installer/AppPayload'
if((Test-Path -LiteralPath (Join-Path $appPayload 'Settings')) -or (Test-Path -LiteralPath (Join-Path $appPayload 'Data'))){throw 'Installer payload contains user data'}
Copy-Item -LiteralPath 'LICENSE','THIRD_PARTY_NOTICES.md' -Destination $appPayload -Force
Copy-Item -LiteralPath 'Licenses' -Destination $appPayload -Recurse -Force
$engine=Join-Path $payload 'Runtime\Ollama'
New-Item -ItemType Directory -Path (Join-Path $engine 'lib\ollama') -Force | Out-Null
Copy-Item -LiteralPath 'Runtime/Ollama/ollama.exe' -Destination $engine
Get-ChildItem 'Runtime/Ollama/lib/ollama' -File | Copy-Item -Destination (Join-Path $engine 'lib\ollama')
# Include the CLI license and documentation; GPU backends are optional, CPU works without a graphics card.
Copy-Item -LiteralPath 'Runtime/Ollama/README.md' -Destination $engine
$modelManifest='Runtime/Ollama/Models/manifests/registry.ollama.ai/library/qwen2.5/1.5b'
Copy-Item -LiteralPath $modelManifest -Destination 'Tools/Installer/model.json'
$model=Get-Content -LiteralPath $modelManifest -Raw | ConvertFrom-Json
$small=@($model.config)+@($model.layers) | Where-Object size -lt 1048576
$blobs=Join-Path $engine 'Models\blobs'
New-Item -ItemType Directory -Path $blobs -Force | Out-Null
foreach($item in $small){ $name=$item.digest.Replace(':','-'); Copy-Item -LiteralPath (Join-Path 'Runtime/Ollama/Models/blobs' $name) -Destination $blobs }
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($pair in @(@('Build/Installer/AppPayload','Build/Installer/app.zip'),@('Build/Installer/RuntimePayload','Build/Installer/runtime.zip'))){
    $source=[IO.Path]::GetFullPath($pair[0]); $dest=[IO.Path]::GetFullPath($pair[1])
    if(Test-Path -LiteralPath $dest){Remove-Item -LiteralPath $dest}
    [IO.Compression.ZipFile]::CreateFromDirectory($source,$dest,[IO.Compression.CompressionLevel]::Optimal,$false)
}
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$pendingPath" '/win32icon:Source\Assets\AppIcon.ico' '/win32manifest:Tools\Installer\Setup.manifest' /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll '/resource:Build\Installer\app.zip,app.zip' '/resource:Build\Installer\runtime.zip,runtime.zip' '/resource:Tools\Installer\model.json,model.json' '/resource:Source\Assets\AppIcon.ico,app.ico' (Join-Path $PSScriptRoot 'Setup.cs') (Join-Path $PSScriptRoot 'SetupUi.cs')
if($LASTEXITCODE -ne 0){throw '安装程序编译失败'}
if(!(Test-Path -LiteralPath $pendingPath) -or (Get-Item -LiteralPath $pendingPath).Length -eq 0){throw 'Installer output is missing'}
Move-Item -LiteralPath $pendingPath -Destination $setupPath -Force
# Replace only published installers, after the new build succeeded.
Get-ChildItem -LiteralPath $releaseRoot -File | Where-Object { $_.Name -match '^MusicTideSetup-v\d+\.\d+\.\d+(\.\d+)?\.exe$' -and $_.FullName -ne $setupPath } | ForEach-Object {
    if([IO.Path]::GetDirectoryName($_.FullName) -ne $releaseRoot){throw 'Release cleanup escaped Distribution'}
    Remove-Item -LiteralPath $_.FullName
}
$hash=Get-FileHash -LiteralPath $setupPath -Algorithm SHA256
$hash | Select-Object Algorithm,Hash,Path | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'Setup-SHA256.json') -Encoding UTF8
$hash | Format-List
