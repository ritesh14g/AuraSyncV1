#!/usr/bin/env bash
# Builds a Roslyn response file for AuraSync's Assembly-CSharp, so C# changes can be
# compile-checked without opening Unity. Usage: gen-rsp.sh <project-root> <out-rsp>
set -euo pipefail

ROOT_WIN="$1"          # e.g. C:/Users/91810/AuraSyncV1
RSP="$2"
CSPROJ="$ROOT_WIN/Assembly-CSharp.csproj"

norm() {
  tr -d '\r' \
    | sed 's/&amp;/\&/g; s/&lt;/</g; s/&gt;/>/g' \
    | sed 's|\\|/|g'
}

: > "$RSP"
{
  echo '-target:library'
  echo '-nostdlib+'
  echo '-noconfig'
  echo '-langversion:9.0'
  echo '-unsafe+'
  echo "-out:$(dirname "$RSP")/out.dll"
} >> "$RSP"

DEF=$(grep -o '<DefineConstants>[^<]*</DefineConstants>' "$CSPROJ" | head -1 | sed 's/<[^>]*>//g' | tr -d '\r')
echo "-define:$DEF" >> "$RSP"

# Sources: only what the csproj lists. Globbing Assets/**/*.cs would wrongly pull in
# Photon demo scripts that live in their own assemblies.
grep -o 'Compile Include="[^"]*"' "$CSPROJ" \
  | sed 's/Compile Include="//; s/"$//' | norm \
  | while IFS= read -r f; do
      [ -n "$f" ] && echo "\"$ROOT_WIN/$f\""
    done >> "$RSP"

# References: HintPath entries may be absolute (the Unity install) or project-relative.
grep -o '<HintPath>[^<]*</HintPath>' "$CSPROJ" \
  | sed 's/<[^>]*>//g' | norm \
  | while IFS= read -r r; do
      [ -z "$r" ] && continue
      case "$r" in
        [A-Za-z]:/*) echo "-r:\"$r\"" ;;
        *)           echo "-r:\"$ROOT_WIN/$r\"" ;;
      esac
    done >> "$RSP"

# PUN, TMP and the rest arrive as <ProjectReference>, so they are missing without these.
for d in "$ROOT_WIN"/Library/ScriptAssemblies/*.dll; do
  base=$(basename "$d")
  case "$base" in
    Assembly-CSharp*) ;;
    *) echo "-r:\"$ROOT_WIN/Library/ScriptAssemblies/$base\"" >> "$RSP" ;;
  esac
done

echo "sources: $(grep -c "^\"" "$RSP")"
echo "refs:    $(grep -c '^-r:' "$RSP")"
echo "double-rooted (should be 0): $(grep -c "AuraSyncV1/[A-Za-z]:" "$RSP" || true)"
