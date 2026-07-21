# 在 Visual Studio 开发者 PowerShell 中执行。
# 首次运行若提示无法执行脚本，可先执行：
#   Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force
#
# 示例：
#   .\build-portable.ps1
#   .\build-portable.ps1 -Architecture arm64
#   .\build-portable.ps1 -Architecture all -Version 1.2.3-beta.1 -Clean

param(
    [ValidateSet("x64", "arm64", "all")]
    [string] $Architecture = "x64",

    [string] $Version = "0.0.1-local",

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [string] $Output = "build/portable",

    [switch] $Clean
)

$ErrorActionPreference = "Stop"
# vpk 为原生进程，非零退出不会自动抛异常。
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot

function Ensure-Vpk {
    if (Get-Command vpk -ErrorAction SilentlyContinue) {
        return
    }

    Write-Host "未找到 vpk，正在安装 Velopack CLI..." -ForegroundColor Yellow
    dotnet tool install -g vpk
    if ($LASTEXITCODE -ne 0) {
        throw "安装 vpk 失败。请手动执行: dotnet tool install -g vpk"
    }

    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "User") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        throw "vpk 已安装但当前会话找不到命令，请重新打开终端后再试。"
    }
}

if ($Clean -and (Test-Path $Output)) {
    Write-Host "清理输出目录: $Output" -ForegroundColor Yellow
    Remove-Item $Output -Recurse -Force
}

$architectures = if ($Architecture -eq "all") { @("x64", "arm64") } else { @($Architecture) }
$portableArtifacts = @()

Ensure-Vpk

foreach ($arch in $architectures) {
    $channel = "win-$arch"
    $pubDir = Join-Path $Output "publish/$arch"
    $releaseDir = Join-Path $Output "releases/$arch"

    Write-Host ""
    Write-Host "== Publish $channel ($Configuration) ==" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $pubDir -Force | Out-Null

    dotnet publish src/Starward/Starward.csproj `
        -c $Configuration `
        -r $channel `
        -p:Platform=$arch `
        -p:Version=$Version `
        -o $pubDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败: $arch" }

    Write-Host "== Pack portable $channel ==" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

    vpk pack `
        --packId Moonward `
        --packVersion $Version `
        --packDir $pubDir `
        --mainExe Moonward.exe `
        --packTitle Moonward `
        --packAuthors "Scighost,TurmoilZoom" `
        --icon src/logo.ico `
        --channel $channel `
        --outputDir $releaseDir `
        --delta BestSpeed
    if ($LASTEXITCODE -ne 0) { throw "vpk pack 失败: $arch" }

    $portableZip = Get-ChildItem -Path $releaseDir -Filter "*-Portable.zip" | Select-Object -First 1
    if ($null -eq $portableZip) {
        throw "未在 $releaseDir 找到便携版 zip"
    }

    $portableArtifacts += $portableZip.FullName
    Write-Host "便携版已生成: $($portableZip.FullName)" -ForegroundColor Green
}

Write-Host ""
Write-Host "全部完成。版本: $Version，配置: $Configuration" -ForegroundColor Green
foreach ($artifact in $portableArtifacts) {
    Write-Host "  $artifact"
}