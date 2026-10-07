#!/usr/bin/env bash
# build_netfx.sh — 在 Linux 上为 ACT 编译插件（目标 .NET Framework 4.7），并审计 API 兼容性。
#
# 不要用 Mono 默认库直接 mcs 编译！Mono 的 mscorlib 含 .NET Core 才有的重载
# （string.Split(char, ...)、TrimStart(char) 等），产物在 Windows ACT 里会抛 MissingMethodException。
# 本脚本改用 -nostdlib + 真正的 .NET Framework 4.7 参考程序集编译，再用 audit_netfx_refs.py 复核。
#
# 用法: build_netfx.sh <输出.dll> <ACT目录或exe> <FFXIV SDK目录|-> <源文件...>
#   FFXIV SDK 目录传 - 表示不引用解析插件 SDK
# 依赖: mono-mcs（apt install mono-mcs）、git、python3 + dnfile
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$1"; act="$2"; sdk="$3"; shift 3

REFASM="${REFASM:-/tmp/refasm/v4.7}"
if [ ! -f "$REFASM/mscorlib.dll" ]; then
  echo "获取 .NET Framework 4.7 参考程序集 (mono/reference-assemblies)…"
  rm -rf /tmp/refasm
  git clone -q --depth 1 --filter=blob:none --sparse https://github.com/mono/reference-assemblies /tmp/refasm
  git -C /tmp/refasm sparse-checkout set v4.7
  REFASM=/tmp/refasm/v4.7
fi

[ -d "$act" ] && act="$act/Advanced Combat Tracker.exe"
refs=(-r:"$REFASM/mscorlib.dll" -r:"$REFASM/System.dll" -r:"$REFASM/System.Core.dll"
      -r:"$REFASM/System.Drawing.dll" -r:"$REFASM/System.Windows.Forms.dll" -r:"$REFASM/System.Xml.dll"
      -r:"$REFASM/System.Runtime.Serialization.dll" -r:"$act")
[ "$sdk" != "-" ] && refs+=(-r:"$sdk/FFXIV_ACT_Plugin.Common.dll")

mkdir -p "$(dirname "$out")"
mcs -nostdlib -noconfig -target:library -optimize+ -out:"$out" "${refs[@]}" "$@"
python3 "$here/audit_netfx_refs.py" "$out" "$REFASM"
echo "构建完成: $out"
