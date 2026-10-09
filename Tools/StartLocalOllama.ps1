param([string]$Model = 'qwen2.5:1.5b', [switch]$Pull)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtimeRoot = Join-Path $projectRoot 'Runtime\Ollama'
$executable = Join-Path $runtimeRoot 'ollama.exe'
if (!(Test-Path -LiteralPath $executable)) {
    $installed = Get-Command ollama -ErrorAction SilentlyContinue
    if ($installed) { $executable = $installed.Source }
    else { throw '未找到 Ollama。请从 https://ollama.com/download/windows 下载，或将官方独立 ZIP 解压至本项目 Runtime\Ollama。' }
}
if ($Model -match 'cloud') { throw '本项目只使用本地模型。' }
New-Item -ItemType Directory -Force -Path $runtimeRoot, (Join-Path $runtimeRoot 'Models') | Out-Null
$previous = @{}
foreach ($name in @('OLLAMA_NO_CLOUD','OLLAMA_MODELS','OLLAMA_HOST')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:OLLAMA_NO_CLOUD = '1'
    $env:OLLAMA_MODELS = Join-Path $runtimeRoot 'Models'
    $env:OLLAMA_HOST = '127.0.0.1:11434'
    $running = $false
    try { $null = Invoke-RestMethod 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2; $running = $true } catch { }
    if (!$running) {
        Start-Process -FilePath $executable -ArgumentList 'serve' -WindowStyle Hidden -WorkingDirectory $runtimeRoot -RedirectStandardOutput (Join-Path $runtimeRoot 'server.log') -RedirectStandardError (Join-Path $runtimeRoot 'server.error.log') | Out-Null
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            Start-Sleep -Milliseconds 500
            try { $null = Invoke-RestMethod 'http://127.0.0.1:11434/api/tags' -TimeoutSec 1; $running = $true; break } catch { }
        }
        if (!$running) { throw 'Ollama 启动失败，请检查 Runtime\Ollama\server.error.log。' }
        Write-Output '已启动本机 Ollama，关闭云端能力，模型保存在本项目 Runtime\Ollama\Models。'
    } else { Write-Output '检测到已有本机 Ollama 服务；沿用其模型存储目录。程序会拒绝云端模型。' }
    if ($Pull) { & $executable pull $Model; if ($LASTEXITCODE -ne 0) { throw '模型下载失败。' } }
    Write-Output '程序入口：音乐玩法 → 关键词学习与本机 AI → 读取本机模型 → 勾选使用本机 Ollama → 保存并立即学习。'
} finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
