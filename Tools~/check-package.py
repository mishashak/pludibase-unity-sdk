#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""UPM 패키지 자가검사.

유니티를 켜지 않고도 "이 패키지가 유니티에 실리는가"를 검사한다.
2026-08-03 현장 연동에서 아래 두 결함으로 패키지 전체가 컴파일되지 않았고,
둘 다 이 검사로 잡힌다.
  1) .meta 누락 -> immutable folder라 유니티가 생성해주지 않아 전 파일 무시
  2) asmdef references 이름 오타 -> 어셈블리 참조 실패

사용:
    python Tools~/check-package.py            # 검사만
    python Tools~/check-package.py --fix-meta # 누락된 .meta 생성까지

종료코드: 0 통과 / 1 실패
"""
import argparse
import json
import os
import re
import sys
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 유니티가 임포트하지 않는 것: '.'로 시작 / '~'로 끝 / 'cvs' / '.tmp'로 끝
# .bak, .orig, .rej 등 편집 잔여물은 유니티엔 보이지만 커밋 대상이 아니므로
# 검사에서도 제외한다(잔여물 때문에 .meta 누락으로 오탐하지 않도록).
LEFTOVER_SUFFIXES = (".bak", ".orig", ".rej", ".swp")


def hidden(name):
    low = name.lower()
    return (
        name.startswith(".")
        or name.endswith("~")
        or low == "cvs"
        or low.endswith(".tmp")
        or low.endswith(LEFTOVER_SUFFIXES)
    )


# 외부 어셈블리 화이트리스트. 여기 없는 참조는 "확인 필요"로 보고한다.
# Talo.Runtime = TaloDev/unity 의 TaloRuntime.asmdef 에 정의된 실제 이름.
KNOWN_EXTERNAL = {
    "Talo.Runtime",
    "Unity.InputSystem",
    "Unity.TextMeshPro",
    "UnityEngine.UI",
}

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

CS_META = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

GENERIC_META = """fileFormatVersion: 2
guid: {guid}
{importer}:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def importer_for(name):
    low = name.lower()
    if low.endswith(".cs"):
        return "CS"
    if low.endswith(".asmdef"):
        return "AssemblyDefinitionImporter"
    if low == "package.json":
        return "PackageManifestImporter"
    if low.endswith((".md", ".txt", ".json")) or low == "license":
        return "TextScriptImporter"
    return "DefaultImporter"


def write_meta(path, is_folder):
    g = uuid.uuid4().hex
    if is_folder:
        body = FOLDER_META.format(guid=g)
    else:
        imp = importer_for(os.path.basename(path))
        body = CS_META.format(guid=g) if imp == "CS" else GENERIC_META.format(guid=g, importer=imp)
    with open(path + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(body)


def walk_visible():
    """유니티가 보는 폴더/파일을 (경로, is_folder)로 낸다."""
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if not hidden(d)]
        if os.path.relpath(dirpath, ROOT) != ".":
            yield dirpath, True
        for fn in filenames:
            if hidden(fn) or fn.endswith(".meta"):
                continue
            yield os.path.join(dirpath, fn), False


def rel(p):
    return os.path.relpath(p, ROOT).replace("\\", "/")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fix-meta", action="store_true", help="누락된 .meta 생성")
    args = ap.parse_args()

    errors, warns, fixed = [], [], []

    # 1. .meta 누락
    for path, is_folder in walk_visible():
        if not os.path.exists(path + ".meta"):
            if args.fix_meta:
                write_meta(path, is_folder)
                fixed.append(rel(path) + ".meta")
            else:
                errors.append(f".meta 누락: {rel(path)}")

    # 2. 고아 .meta (대상이 없는 것)
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if not hidden(d)]
        for fn in filenames:
            if not fn.endswith(".meta"):
                continue
            target = os.path.join(dirpath, fn[:-5])
            if not os.path.exists(target):
                errors.append(f"고아 .meta (대상 없음): {rel(os.path.join(dirpath, fn))}")

    # 3. GUID 형식 / 중복
    seen = {}
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if not hidden(d)]
        for fn in filenames:
            if not fn.endswith(".meta"):
                continue
            p = os.path.join(dirpath, fn)
            txt = open(p, encoding="utf-8").read()
            m = re.search(r"^guid: ([0-9a-fA-F]{32})$", txt, re.M)
            if not m:
                errors.append(f"GUID 형식 오류(32자리 16진수 아님): {rel(p)}")
                continue
            g = m.group(1)
            if g in seen:
                errors.append(f"GUID 중복: {rel(p)} == {seen[g]}")
            seen[g] = rel(p)

    # 4. asmdef 참조 검사
    asmdefs = {}
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if not hidden(d)]
        for fn in filenames:
            if fn.endswith(".asmdef"):
                p = os.path.join(dirpath, fn)
                try:
                    data = json.load(open(p, encoding="utf-8"))
                except Exception as e:
                    errors.append(f"asmdef JSON 파싱 실패: {rel(p)} ({e})")
                    continue
                asmdefs[data.get("name", "")] = (rel(p), data)

    internal = set(asmdefs.keys())
    for name, (path, data) in asmdefs.items():
        if not name:
            errors.append(f"asmdef에 name 없음: {path}")
        for ref in data.get("references", []):
            if ref.startswith("GUID:"):
                continue
            if ref in internal or ref in KNOWN_EXTERNAL:
                continue
            errors.append(
                f"asmdef 참조가 실재하지 않는 이름일 수 있음: {path} -> \"{ref}\" "
                f"(내부: {sorted(internal)}, 알려진 외부: {sorted(KNOWN_EXTERNAL)})"
            )

    # 5. asmdef 없는 곳의 .cs (어느 어셈블리에도 안 들어감)
    asmdef_dirs = [os.path.dirname(os.path.join(ROOT, p)) for p, _ in asmdefs.values()]
    for path, is_folder in walk_visible():
        if is_folder or not path.endswith(".cs"):
            continue
        if not any(os.path.commonpath([path, d]) == d for d in asmdef_dirs):
            warns.append(f"asmdef 범위 밖 .cs (기본 어셈블리로 감): {rel(path)}")

    # 6. package.json
    pj = os.path.join(ROOT, "package.json")
    if not os.path.exists(pj):
        errors.append("package.json 없음")
    else:
        try:
            data = json.load(open(pj, encoding="utf-8"))
            for k in ("name", "version", "displayName", "unity"):
                if k not in data:
                    errors.append(f"package.json 필수 필드 누락: {k}")
            if "name" in data and not re.match(r"^[a-z0-9.\-]+$", data["name"]):
                errors.append(f"package.json name 형식 오류: {data['name']}")
        except Exception as e:
            errors.append(f"package.json 파싱 실패: {e}")

    # 결과
    if fixed:
        print(f"[생성] .meta {len(fixed)}개")
        for f in sorted(fixed):
            print("   +", f)
        print()
    for w in warns:
        print("[경고]", w)
    for e in errors:
        print("[실패]", e)

    if errors:
        print(f"\n실패 {len(errors)}건, 경고 {len(warns)}건")
        return 1
    print(f"통과. 경고 {len(warns)}건.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
