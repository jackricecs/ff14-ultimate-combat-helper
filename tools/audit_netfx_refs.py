#!/usr/bin/env python3
"""
audit_netfx_refs.py — 检查编译好的插件 DLL 是否只调用了 .NET Framework 4.7 中真实存在的 API。

背景：在 Linux 上用 Mono 的 mcs 默认编译时，Mono 的 mscorlib 含有 .NET Core 才有的重载
（如 string.Split(char, StringSplitOptions)、string.TrimStart(char)），编译能过，
但在 Windows 的 ACT 里加载会抛 System.MissingMethodException。此脚本把 DLL 中引用的
每个 BCL 成员与真正的 .NET Framework 4.7 参考程序集比对，找出不存在的成员。

依赖: pip install dnfile；参考程序集来自 github.com/mono/reference-assemblies（v4.7 目录）
用法: python3 audit_netfx_refs.py <插件.dll> <refasm/v4.7 目录>
退出码: 0 = 全部存在, 1 = 发现缺失成员
"""
import os
import sys

import dnfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dump_dotnet_api import Sig  # noqa: E402

BCL = ["mscorlib", "System", "System.Core", "System.Drawing", "System.Windows.Forms", "System.Xml", "System.Data", "System.Runtime.Serialization"]


def sig_str(pe, raw):
    b = bytes(raw.value if hasattr(raw, "value") else raw)
    if b[0] & 0x0F == 0x06:
        return "field " + Sig(pe, b).field()
    ret, args = Sig(pe, b).method()
    return f"{ret}({', '.join(args)})"


def index_refasm(folder):
    have = set()
    for name in BCL:
        p = os.path.join(folder, name + ".dll")
        if not os.path.exists(p):
            continue
        pe = dnfile.dnPE(p)
        for t in pe.net.mdtables.TypeDef:
            full = f"{t.TypeNamespace}.{t.TypeName}"
            for m in (t.MethodList or []):
                try:
                    have.add(f"{full}::{m.row.Name} {sig_str(pe, m.row.Signature)}")
                except Exception:
                    pass
            for f in (t.FieldList or []):
                try:
                    have.add(f"{full}::{f.row.Name} {sig_str(pe, f.row.Signature)}")
                except Exception:
                    pass
    return have


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    dll, refdir = sys.argv[1], sys.argv[2]
    have = index_refasm(refdir)
    pe = dnfile.dnPE(dll)
    md = pe.net.mdtables
    missing, checked = [], 0
    for m in (md.MemberRef or []):
        try:
            parent = m.Class.row
            if not hasattr(parent, "ResolutionScope"):
                continue                    # TypeSpec（泛型实例等）略过
            scope = parent.ResolutionScope.row
            asm = str(getattr(scope, "Name", ""))
            if asm not in BCL:
                continue                    # 只检查 BCL；ACT/SDK 由引用编译保证
            full = f"{parent.TypeNamespace}.{parent.TypeName}"
            key = f"{full}::{m.Name} {sig_str(pe, m.Signature)}"
            checked += 1
            if key not in have:
                missing.append(f"[{asm}] {key}")
        except Exception:
            continue
    print(f"检查了 {checked} 个 BCL 成员引用")
    if missing:
        print("以下成员在 .NET Framework 4.7 中不存在（会导致 MissingMethodException）：")
        for x in sorted(set(missing)):
            print("  " + x)
        sys.exit(1)
    print("OK：全部存在于 .NET Framework 4.7")


if __name__ == "__main__":
    main()
