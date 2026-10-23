param(
    [string]$Configuration = "Release",
    [switch]$SkipCli
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "==> 构建图形界面" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\McInstall.App\McInstall.App.csproj") `
    -c $Configuration -r win-x64 --self-contained false -p:PublishSingleFile=true `
    -o (Join-Path $root "dist") -v q --nologo

if (-not $SkipCli) {
    Write-Host "==> 构建命令行版" -ForegroundColor Cyan
    dotnet publish (Join-Path $root "src\McInstall.Cli\McInstall.Cli.csproj") `
        -c $Configuration -r win-x64 --self-contained false -p:PublishSingleFile=true `
        -o (Join-Path $root "dist\cli") -v q --nologo
}

Get-ChildItem (Join-Path $root "dist") -File | Format-Table Length, Name -AutoSize
Write-Host "完成。把 dist\McInstall.exe 放到 PCL2.exe 同目录即可。" -ForegroundColor Green
