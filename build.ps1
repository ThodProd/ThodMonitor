<#
  Сборка Thod Monitor.
  Примеры:
    .\build.ps1                      # self-contained, win-x64, папка publish\
    .\build.ps1 -SelfContained:$false  # меньше размер, но нужен установленный .NET Desktop Runtime
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true
)
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "src\ThodMonitor\ThodMonitor.csproj"
$out  = Join-Path $PSScriptRoot "publish"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "Не найден .NET SDK. Установите .NET SDK (LTS) с https://dotnet.microsoft.com/download"
}
Write-Host "dotnet $(dotnet --version)"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

# Не упаковываем в один файл: библиотека датчиков содержит нативные/драйверные компоненты.
dotnet publish $proj -c $Configuration -r $Runtime `
    --self-contained $($SelfContained.ToString().ToLower()) `
    -p:PublishSingleFile=false -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw "Сборка завершилась с ошибкой ($LASTEXITCODE)" }

Write-Host "`nГотово: $out\ThodMonitor.exe"
Write-Host "Копируйте всю папку publish целиком, а не только EXE."
