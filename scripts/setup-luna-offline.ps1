$ErrorActionPreference = "Stop"

Write-Host "==============================================="
Write-Host " LUNA PC - Preparação do cérebro local"
Write-Host "==============================================="
Write-Host ""
Write-Host "Este processo precisa de internet apenas uma vez para instalar o motor local e baixar o modelo." -ForegroundColor Yellow
Write-Host "Depois disso, o cérebro da LUNA poderá funcionar sem internet."
Write-Host ""

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

Write-Host "Iniciando o motor local..." -ForegroundColor Cyan
Start-Process -FilePath $ollamaExe -ArgumentList "serve" -WindowStyle Hidden -ErrorAction SilentlyContinue | Out-Null
Start-Sleep -Seconds 3

Write-Host "Baixando o cérebro local Qwen3 4B (aprox. 2,5 GB)..." -ForegroundColor Cyan
& $ollamaExe pull qwen3:4b-instruct

Write-Host ""
Write-Host "LUNA PC está preparada para usar o cérebro local." -ForegroundColor Green
Write-Host "Modelo: qwen3:4b-instruct"
Write-Host "Endpoint local: http://127.0.0.1:11434"
Write-Host ""
Write-Host "A partir daqui, as conversas da LUNA não precisam sair do computador." -ForegroundColor Green
Write-Host "Você pode desconectar a internet e testar a LUNA PC."
Write-Host ""
