#!/usr/bin/env python3
"""
dump_dotnet_api.py — 在 Linux/macOS 上列出 .NET 程序集（DLL/EXE）的类型、方法签名、属性、字段、事件。
用于研究闭源的 FFXIV_ACT_Plugin SDK、ACT 主程序 API 等，无需 Windows / ILSpy。

依赖: pip install dnfile   (若受限环境: pip install dnfile --break-system-packages)

用法:
  python3 dump_dotnet_api.py <dll> [--ns 命名空间前缀] [--type 类型名子串] [--all]
示例:
  python3 dump_dotnet_api.py SDK/FFXIV_ACT_Plugin.Common.dll
  python3 dump_dotnet_api.py "Advanced Combat Tracker.exe" --type FormActMain
默认只输出 public 类型与 public 成员；--all 输出全部（含编译器生成成员）。
"""
import argparse
import sys

try:
    import dnfile
except ImportError:
    sys.exit("需要 dnfile: pip install dnfile")

PRIM = {0x01: "void", 0x02: "bool", 0x03: "char", 0x04: "sbyte", 0x05: "byte", 0x06: "short",
        0x07: "ushort", 0x08: "int", 0x09: "uint", 0x0a: "long", 0x0b: "ulong", 0x0c: "float",
        0x0d: "double", 0x0e: "string", 0x18: "IntPtr", 0x19: "UIntPtr", 0x1c: "object", 0x16: "TypedReference"}


class Sig:
    def __init__(self, pe, data):
        self.pe, self.d, self.i = pe, bytes(data), 0

    def u8(self):
        b = self.d[self.i]; self.i += 1; return b

    def cint(self):
        b = self.u8()
        if b & 0x80 == 0: return b
        if b & 0xC0 == 0x80: return ((b & 0x3F) << 8) | self.u8()
        return ((b & 0x1F) << 24) | (self.u8() << 16) | (self.u8() << 8) | self.u8()

    def typename_from_coded(self, coded):
        tag, idx = coded & 3, coded >> 2
        md = self.pe.net.mdtables
        try:
            if tag == 0:
                t = md.TypeDef[idx - 1]; return str(t.TypeName)
            if tag == 1:
                t = md.TypeRef[idx - 1]; return str(t.TypeName)
        except Exception:
            pass
        return "?spec"

    def type(self):
        et = self.u8()
        if et in PRIM: return PRIM[et]
        if et in (0x1f, 0x20): self.cint(); return self.type()
        if et == 0x10: return "ref " + self.type()
        if et == 0x0f: return self.type() + "*"
        if et in (0x11, 0x12): return self.typename_from_coded(self.cint())
        if et == 0x1d: return self.type() + "[]"
        if et == 0x13: return "T" + str(self.cint())
        if et == 0x1e: return "M" + str(self.cint())
        if et == 0x14:
            t = self.type(); rank = self.cint()
            n = self.cint(); [self.cint() for _ in range(n)]
            n = self.cint(); [self.cint() for _ in range(n)]
            return t + "[" + "," * (rank - 1) + "]"
        if et == 0x15:
            self.u8(); base = self.typename_from_coded(self.cint())
            n = self.cint(); args = [self.type() for _ in range(n)]
            return base.split("`")[0] + "<" + ", ".join(args) + ">"
        if et == 0x1b: return "fnptr"
        return f"?0x{et:x}"

    def method(self):
        flags = self.u8()
        if flags & 0x10: self.cint()  # generic param count
        n = self.cint(); ret = self.type()
        return ret, [self.type() for _ in range(n)]

    def field(self):
        self.u8(); return self.type()

    def prop(self):
        self.u8(); n = self.cint(); return self.type()


def is_public_type(t):
    vis = int(t.Flags) & 0x7 if not hasattr(t.Flags, "tdPublic") else None
    try:
        return bool(t.Flags.tdPublic or t.Flags.tdNestedPublic)
    except Exception:
        return vis in (1, 2)


def is_public_method(m):
    try:
        return bool(m.Flags.mdPublic)
    except Exception:
        return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dll"); ap.add_argument("--ns"); ap.add_argument("--type"); ap.add_argument("--all", action="store_true")
    a = ap.parse_args()
    pe = dnfile.dnPE(a.dll)
    md = pe.net.mdtables
    for asm in (md.Assembly or []):
        print(f"# Assembly {asm.Name} {asm.MajorVersion}.{asm.MinorVersion}.{asm.BuildNumber}.{asm.RevisionNumber}")
    for r in (md.AssemblyRef or []):
        print(f"#   ref {r.Name} {r.MajorVersion}.{r.MinorVersion}")
    # property map: TypeDef row index -> properties
    propmap = {}
    for pm in (md.PropertyMap or []):
        try:
            propmap[pm.Parent.row_index] = [p.row for p in pm.PropertyList]
        except Exception:
            pass
    for ti, t in enumerate(md.TypeDef, start=1):
        ns, name = str(t.TypeNamespace), str(t.TypeName)
        if name == "<Module>": continue
        if a.ns and not ns.startswith(a.ns): continue
        if a.type and a.type.lower() not in name.lower(): continue
        if not a.all and not is_public_type(t): continue
        base = ""
        try:
            if getattr(t.Flags, "tdInterface", False): base = " (interface)"
            elif t.Extends and t.Extends.row_index: base = " : " + str(t.Extends.row.TypeName)
        except Exception:
            pass
        print(f"\n{ns}.{name}{base}")
        for f in (t.FieldList or []):
            fr = f.row
            if not a.all and not getattr(fr.Flags, "fdPublic", True): continue
            try: ft = Sig(pe, fr.Signature.value if hasattr(fr.Signature, "value") else fr.Signature).field()
            except Exception: ft = "?"
            print(f"  field {ft} {fr.Name}")
        for p in propmap.get(ti, []):
            try: pt = Sig(pe, p.Type.value if hasattr(p.Type, "value") else p.Type).prop()
            except Exception: pt = "?"
            print(f"  prop  {pt} {p.Name}")
        for m in (t.MethodList or []):
            mr = m.row; mn = str(mr.Name)
            if not a.all and (not is_public_method(mr) or mn.startswith(("get_", "set_", "<"))): continue
            try: ret, args = Sig(pe, mr.Signature.value if hasattr(mr.Signature, "value") else mr.Signature).method()
            except Exception: ret, args = "?", []
            pnames = [str(p.row.Name) for p in (mr.ParamList or []) if p.row.Sequence > 0]
            params = ", ".join(f"{ty} {pnames[i] if i < len(pnames) else ''}".strip() for i, ty in enumerate(args))
            kind = "event+" if mn.startswith("add_") else "method"
            print(f"  {kind} {ret} {mn}({params})")


if __name__ == "__main__":
    main()
