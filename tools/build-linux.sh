#!/usr/bin/env bash
# build-linux.sh — 在 Linux 上完整构建 + 测试（需要 mono-mcs、mono-runtime、git、python3、dnfile）
#   apt install mono-mcs mono-runtime libmono-system-runtime-serialization4.0-cil
#   pip install dnfile
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"; cd "$root"
bash tools/fetch-deps.sh
src=$(find src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*')
core=$(find src/UCH.Core -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*')
for v in cn global; do
  bash tools/build_netfx.sh "artifacts/$v/UltimateCombatHelper.dll" thirdparty/ACT "thirdparty/sdk-$v/SDK" $src
done
R="${REFASM:-/tmp/refasm/v4.7}"
mcs -nostdlib -noconfig -out:artifacts/UCH.Core.Tests.exe -r:$R/mscorlib.dll -r:$R/System.dll -r:$R/System.Core.dll \
    -r:$R/System.Runtime.Serialization.dll -r:$R/System.Xml.dll $core tests/UCH.Core.Tests/Program.cs
python3 tools/audit_netfx_refs.py artifacts/UCH.Core.Tests.exe "$R" >/dev/null
mono artifacts/UCH.Core.Tests.exe
