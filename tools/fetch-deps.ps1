# fetch-deps.ps1 — 下载构建所需的第三方程序集到 thirdparty/（不提交到仓库）
#   ACT 主程序（引用用）、国服解析插件 SDK（无强命名）、国际服解析插件 SDK（有强命名）
# 版本号集中在这里维护；升级时改这三个变量，并更新 docs/development.md 的版本说明。
param(
    [string]$ActVersion = "3.8.5.288",
    [string]$CnSdkFile = "FFXIV_ACT_Plugin_SDK_3.0.2.7.zip",
    [string]$GlobalSdkFile = "FFXIV_ACT_Plugin_SDK_3.0.3.1.zip"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tp = Join-Path $root "thirdparty"
New-Item -ItemType Directory -Force -Path $tp | Out-Null

function Get-Zip($url, $dest) {
    if (Test-Path $dest) { Write-Host "已存在，跳过: $dest"; return }
    $zip = "$dest.zip"
    Write-Host "下载 $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $dest -Force
    Remove-Item $zip
}

Get-Zip "https://github.com/EQAditu/AdvancedCombatTracker/releases/download/$ActVersion/ACTv3.zip" (Join-Path $tp "ACT")
Get-Zip "https://github.com/TundraWork/FFXIV_ACT_Plugin_CN/raw/master/$CnSdkFile" (Join-Path $tp "sdk-cn")
Get-Zip "https://github.com/ravahn/FFXIV_ACT_Plugin/raw/master/Releases/$GlobalSdkFile" (Join-Path $tp "sdk-global")

# SDK 压缩包里路径可能用反斜杠，统一检查 Common.dll 位置
foreach ($v in "sdk-cn", "sdk-global") {
    $dll = Get-ChildItem -Recurse -Filter "FFXIV_ACT_Plugin.Common.dll" (Join-Path $tp $v) | Select-Object -First 1
    if (-not $dll) { throw "$v 中未找到 FFXIV_ACT_Plugin.Common.dll" }
    $want = Join-Path $tp "$v\SDK"
    if ($dll.DirectoryName -ne $want) {
        New-Item -ItemType Directory -Force -Path $want | Out-Null
        Copy-Item (Join-Path $dll.DirectoryName "*.dll") $want -Force
    }
    Write-Host "$v SDK: $want"
}
Write-Host "完成。"
