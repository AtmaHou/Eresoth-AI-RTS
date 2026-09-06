#!/usr/bin/env bash
# Unity 编辑器打开时的离线编译检查：用 dotnet Roslyn + Unity 官方引用 DLL 编译项目脚本。
# 用法: bash AI_RTS/compile_check.sh   （在仓库任意目录执行均可）
set -u
PROJ="$(cd "$(dirname "$0")/EresothRTS" && pwd)"
UE="/d/App/Game/unityhub/unity6/Editor/Data/Managed/UnityEngine"
NS="/d/App/Game/unityhub/unity6/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll"
CSC="/c/Program Files/dotnet/sdk/10.0.400/Roslyn/bincore/csc.dll"
OUT="$(mktemp -d)"
fail=0

REFS=(); for f in "$UE"/UnityEngine.*.dll; do REFS+=("-r:$f"); done
EREFS=(); for f in "$UE"/UnityEditor.*.dll; do EREFS+=("-r:$f"); done

echo "== Assembly-CSharp (Assets/Scripts) =="
dotnet "$CSC" -nologo -t:library -nostdlib -r:"$NS" "${REFS[@]}" -out:"$OUT/AC.dll" "$PROJ"/Assets/Scripts/*.cs || fail=1

echo "== Assembly-CSharp-Editor (Assets/Editor) =="
dotnet "$CSC" -nologo -t:library -nostdlib -r:"$NS" "${REFS[@]}" "${EREFS[@]}" -r:"$OUT/AC.dll" -out:"$OUT/ACE.dll" "$PROJ"/Assets/Editor/*.cs || fail=1

rm -rf "$OUT"
if [ "$fail" = "0" ]; then echo "COMPILE OK"; else echo "COMPILE FAILED"; exit 1; fi
