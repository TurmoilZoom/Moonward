# visual studio中进入开发者powershell执行
# 执行前，允许执行该未签名脚本：Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force
param(
    [string] $Architecture = "x64",
    [string] $Version = "1.0.0",
    [string] $Output = "build/Starward"
)

$ErrorActionPreference = "Stop";

dotnet publish src/Starward -c Release -r "win-$Architecture" -o "$Output/app-$Version" -p:Platform=$Architecture -p:Version=$Version;

msbuild src/Starward.Launcher "-property:Configuration=Release;Platform=$Architecture;OutDir=$(Resolve-Path "$Output/")";

Set-Content "$Output/version.ini" -Value "version=$Version";

Remove-Item "$Output/Starward.pdb" -Force;
