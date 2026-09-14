$ErrorActionPreference = "Stop"

Write-Host "==============================================="
Write-Host " LUNA PC - Preparação offline"
Write-Host "==============================================="
Write-Host ""
Write-Host "A internet será necessária apenas nesta preparação inicial." -ForegroundColor Yellow
Write-Host "Depois do download, cérebro e voz poderão funcionar localmente." -ForegroundColor Yellow
Write-Host ""

# ------------------------------------------------
# 1. CÉREBRO LOCAL — Ollama + Qwen3 4B
# ------------------------------------------------
$ollamaCommand = Get-Command ollama -ErrorAction SilentlyContinue

if (-not $ollamaCommand) {
    Write-Host "Ollama não foi encontrado. Tentando instalar pelo Windows Package Manager..." -ForegroundColor Cyan
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "Ollama não está instalado e o winget não está disponível. Instale o Ollama para Windows e execute este script novamente."
    }

    winget install --id Ollama.Ollama -e --accept-source-agreements --accept-package-agreements
}

$ollamaCommand = Get-Command ollama -ErrorAction SilentlyContinue
if (-not $ollamaCommand) {
    $possible = @(
        "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe",
        "$env:ProgramFiles\Ollama\ollama.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($possible) {
        $ollamaExe = $possible
    } else {
        throw "Ollama foi instalado, mas o executável não foi encontrado. Reinicie o Windows e execute este script novamente."
    }
} else {
    $ollamaExe = $ollamaCommand.Source
}

Write-Host "Iniciando o motor local do cérebro..." -ForegroundColor Cyan
Start-Process -FilePath $ollamaExe -ArgumentList "serve" -WindowStyle Hidden -ErrorAction SilentlyContinue | Out-Null
Start-Sleep -Seconds 3

Write-Host "Baixando Qwen3 4B (aprox. 2,5 GB)..." -ForegroundColor Cyan
& $ollamaExe pull qwen3:4b-instruct

# ------------------------------------------------
# 2. VOZ NEURAL LOCAL — Piper TTS pt-BR
# ------------------------------------------------
$ttsRoot = Join-Path $env:LOCALAPPDATA "LunaPC\tts"
$piperDir = Join-Path $ttsRoot "piper"
$voicesDir = Join-Path $ttsRoot "voices"
$audioDir = Join-Path $ttsRoot "audio"
New-Item -ItemType Directory -Force -Path $piperDir, $voicesDir, $audioDir | Out-Null

$piperExe = Join-Path $piperDir "piper.exe"
$voiceModel = Join-Path $voicesDir "pt_BR-faber-medium.onnx"
$voiceConfig = Join-Path $voicesDir "pt_BR-faber-medium.onnx.json"
$piperZip = Join-Path $env:TEMP "luna-piper-windows.zip"

if (-not (Test-Path $piperExe)) {
    Write-Host "Baixando o motor de voz neural Piper para Windows..." -ForegroundColor Cyan
    $piperUrl = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip"
    Invoke-WebRequest -Uri $piperUrl -OutFile $piperZip

    $extractDir = Join-Path $env:TEMP "luna-piper-extract"
    if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
    Expand-Archive -Path $piperZip -DestinationPath $extractDir -Force

    $foundPiper = Get-ChildItem -Path $extractDir -Filter "piper.exe" -Recurse | Select-Object -First 1
    if (-not $foundPiper) { throw "Não encontrei piper.exe dentro do pacote baixado." }

    # O Piper precisa do executável e das DLLs/recursos que vêm junto no diretório.
    Copy-Item (Join-Path $foundPiper.Directory.FullName "*") $piperDir -Recurse -Force

    Remove-Item $extractDir -Recurse -Force
    Remove-Item $piperZip -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $voiceModel)) {
    Write-Host "Baixando voz neural em português do Brasil (aprox. 63 MB)..." -ForegroundColor Cyan
    $voiceBase = "https://huggingface.co/rhasspy/piper-voices/resolve/main/pt/pt_BR/faber/medium"
    Invoke-WebRequest -Uri "$voiceBase/pt_BR-faber-medium.onnx" -OutFile $voiceModel
    Invoke-WebRequest -Uri "$voiceBase/pt_BR-faber-medium.onnx.json" -OutFile $voiceConfig
}

Write-Host ""
Write-Host "===============================================" -ForegroundColor Green
Write-Host " LUNA PC está preparada!" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Green
Write-Host "Cérebro: Qwen3 4B local"
Write-Host "Voz: Piper neural pt-BR"
Write-Host "Local dos recursos: $ttsRoot"
Write-Host ""
Write-Host "Depois disso, você pode desconectar a internet e testar." -ForegroundColor Green
Write-Host ""
