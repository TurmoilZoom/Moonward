# 打出与 GitHub Release 相同形态的 Velopack 安装包（Setup.exe + 便携 zip）。
# 对齐 .github/workflows/release.yml 的 publish / vpk pack 参数。
# 在仓库根目录执行。首次若无法跑脚本：
#   Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force
#
# 示例：
#   .\build-setup.ps1
#   .\build-setup.ps1 -Architecture arm64
#   .\build-setup.ps1 -Version 0.0.1-local -Clean
#
# 注意：packId 与正式版同为 Moonward，安装会占用同一应用身份。
# 本地包请用带 -local 的版本号，避免和 GitHub 已装版本搅在一起。

param(
    [ValidateSet("x64", "arm64", "all")]
    [string] $Architecture = "x64",

    [string] $Version = "",

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [string] $Output = "build/setup",

    [switch] $Clean,

    [switch] $OpenOutput
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $sha = (git rev-parse --short HEAD 2>$null)
    if ([string]::IsNullOrWhiteSpace($sha)) { $sha = "dev" }
    $Version = "0.0.1-local.$sha"
}

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
$setupArtifacts = @()

Ensure-Vpk

foreach ($arch in $architectures) {
    $channel = "win-$arch"
    $pubDir = Join-Path $Output "publish/$arch"
    $releaseDir = Join-Path $Output "releases/$arch"

    Write-Host ""
    Write-Host "== Publish $channel ($Configuration, $Version) ==" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $pubDir -Force | Out-Null

    dotnet publish src/Starward/Starward.csproj `
        -c $Configuration `
        -r $channel `
        -p:Platform=$arch `
        -p:Version=$Version `
        -o $pubDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败: $arch" }

    Copy-Item -LiteralPath (Join-Path $RepoRoot "LICENSE") -Destination (Join-Path $pubDir "LICENSE") -Force

    Write-Host "== Pack Setup $channel ==" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

    vpk pack `
        --packId Moonward `
        --packVersion $Version `
        --packDir $pubDir `
        --mainExe Moonward.exe `
        --packTitle Moonward `
        --packAuthors "Scighost,TurmoilZoom" `
        --icon src/logo.ico `
        --instLicense (Join-Path $pubDir "LICENSE") `
        --channel $channel `
        --outputDir $releaseDir `
        --delta None
    if ($LASTEXITCODE -ne 0) { throw "vpk pack 失败: $arch" }

    $setup = Get-ChildItem -Path $releaseDir -Filter "*-Setup.exe" | Select-Object -First 1
    if ($null -eq $setup) {
        throw "未在 $releaseDir 找到 Setup.exe"
    }
    $setupArtifacts += $setup.FullName
    Write-Host "安装包已生成: $($setup.FullName)" -ForegroundColor Green
}

Write-Host ""
Write-Host "全部完成。版本: $Version，配置: $Configuration" -ForegroundColor Green
foreach ($artifact in $setupArtifacts) {
    Write-Host "  $artifact"
}

if ($OpenOutput -and $setupArtifacts.Count -gt 0) {
    Start-Process explorer.exe -ArgumentList "/select,`"$($setupArtifacts[0])`""
}
