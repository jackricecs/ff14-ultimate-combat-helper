#!/usr/bin/env bash
# fetch-deps.sh — Linux/macOS 版依赖下载（与 fetch-deps.ps1 等价），供 tools/build_netfx.sh 使用
set -euo pipefail
ACT_VERSION="${ACT_VERSION:-3.8.5.288}"
CN_SDK="${CN_SDK:-FFXIV_ACT_Plugin_SDK_3.0.2.7.zip}"
GLOBAL_SDK="${GLOBAL_SDK:-FFXIV_ACT_Plugin_SDK_3.0.3.1.zip}"
root="$(cd "$(dirname "$0")/.." && pwd)"; tp="$root/thirdparty"; mkdir -p "$tp"
get() { [ -d "$2" ] && { echo "已存在: $2"; return; }; echo "下载 $1"; curl -fsSL -o "$2.zip" "$1"; mkdir -p "$2"; unzip -oq "$2.zip" -d "$2" 2>/dev/null || true; rm -f "$2.zip"; }
get "https://github.com/EQAditu/AdvancedCombatTracker/releases/download/$ACT_VERSION/ACTv3.zip" "$tp/ACT"
get "https://github.com/TundraWork/FFXIV_ACT_Plugin_CN/raw/master/$CN_SDK" "$tp/sdk-cn"
get "https://github.com/ravahn/FFXIV_ACT_Plugin/raw/master/Releases/$GLOBAL_SDK" "$tp/sdk-global"
for v in sdk-cn sdk-global; do
  f=$(find "$tp/$v" -iname 'FFXIV_ACT_Plugin.Common.dll' | head -1)
  [ -z "$f" ] && { echo "$v 未找到 Common.dll"; exit 1; }
  mkdir -p "$tp/$v/SDK"; [ "$(dirname "$f")" != "$tp/$v/SDK" ] && cp "$(dirname "$f")"/*.dll "$tp/$v/SDK/"
  echo "$v SDK: $tp/$v/SDK"
done
