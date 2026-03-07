$ErrorActionPreference = "Stop"

function Write-Step($msg) {
    Write-Host "[setup] $msg" -ForegroundColor Cyan
}

function Ensure-Winget {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget не найден. Установите App Installer из Microsoft Store и повторите."
    }
}

function Install-WingetPackage {
    param(
        [Parameter(Mandatory = $true)] [string] $Id,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    Write-Step "Проверка: $Name"
    $list = winget list --id $Id --accept-source-agreements 2>$null | Out-String
    if ($list -match [regex]::Escape($Id)) {
        Write-Step "$Name уже установлен"
        return
    }

    Write-Step "Установка: $Name"
    winget install --id $Id --exact --accept-package-agreements --accept-source-agreements --silent --disable-interactivity
}

try {
    Write-Step "Запуск автонастройки для AudioChat"
    Ensure-Winget

    Install-WingetPackage -Id "Google.Chrome" -Name "Google Chrome"
    Install-WingetPackage -Id "Microsoft.DotNet.SDK.9" -Name ".NET SDK 9"

    # Voicemeeter устанавливает виртуальные аудио endpoints (Input/Output), которые можно использовать как кабели.
    Install-WingetPackage -Id "VB-Audio.Voicemeeter" -Name "VB-Audio Voicemeeter"

    Write-Host ""
    Write-Host "Готово. Если после установки нет новых Voicemeeter/CABLE устройств, перезагрузите ПК." -ForegroundColor Yellow
    Write-Host "После перезагрузки снова запустите приложение и выберите голосовой режим." -ForegroundColor Yellow
}
catch {
    Write-Host "Ошибка автонастройки: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
