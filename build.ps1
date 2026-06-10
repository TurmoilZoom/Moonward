# visual studio中进入开发者powershell执行
# 执行前，允许执行该未签名脚本：Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force
param(
    [string] $Architecture = "x64",
    [string] $Version = "1.0.0",
    [string] $Output = "build/Starward"
)

$ErrorActionPreference = "Stop";

# 干净构建：执行前自动删除 build 目录
$buildRoot = Split-Path $Output -Parent
if ([string]::IsNullOrWhiteSpace($buildRoot)) { $buildRoot = $Output }
if (Test-Path $buildRoot -PathType Container) {
    Write-Host "Cleaning $buildRoot for clean build..." -ForegroundColor Yellow
    Remove-Item $buildRoot -Recurse -Force
}

# 确保输出目录存在（dotnet publish 和 msbuild 需要）
New-Item -ItemType Directory -Path $Output -Force | Out-Null

dotnet publish src/Starward -c Release -r "win-$Architecture" -o "$Output/app-$Version" -p:Platform=$Architecture -p:Version=$Version;

msbuild src/Starward.Launcher "-property:Configuration=Release;Platform=$Architecture;OutDir=$(Resolve-Path "$Output/")";

Set-Content "$Output/version.ini" -Value "version=$Version";

Remove-Item "$Output/Starward.pdb" -Force;
