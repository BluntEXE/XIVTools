#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-1.0.0}"
REPO="$(cd "$(dirname "$0")" && pwd)"
OUTDIR="$REPO/../xivtools-release"
GUI="$REPO/xivtools-ui"
CONV="$REPO/xivtools-converters"

echo "Building XIVTools v${VERSION} release packages..."

rm -rf "$OUTDIR"
mkdir -p "$OUTDIR"

# ── Build helpers ─────────────────────────────────────────────────────────────

build_gui() {
    local rid="$1" name="$2"
    echo "  Building GUI for $rid..."
    dotnet publish "$GUI" \
        -c Release \
        -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=false \
        -p:PublishReadyToRun=true \
        -o "$OUTDIR/$name/app" \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:Version="$VERSION" \
        2>&1 | { grep -E "error|warning|published" || true; }
}

build_converter() {
    local proj="$1" rid="$2" destdir="$3" binname="$4"
    echo "  Building $proj for $rid..."
    local tmp="$OUTDIR/tmp_conv"
    rm -rf "$tmp"
    dotnet publish "$CONV/$proj" \
        -c Release \
        -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -o "$tmp" \
        2>&1 | { grep -E "error|published" || true; }
    mkdir -p "$destdir"
    cp "$tmp/converter"* "$destdir/$binname" 2>/dev/null || \
    cp "$tmp/converter.exe" "$destdir/$binname" 2>/dev/null || \
    find "$tmp" -maxdepth 1 -name "converter*" -exec cp {} "$destdir/$binname" \;
    rm -rf "$tmp"
}

assemble() {
    local name="$1" rid="$2" exe="$3"

    echo "Assembling $name..."
    local base="$OUTDIR/$name"

    # OBJ converter
    build_converter ObjConverter "$rid" "$base/app/converters/obj" "$exe"

    # Assimp converter (shared binary for fbx/glb/gltf/dae)
    build_converter AssimpConverter "$rid" "$base/app/converters/fbx" "$exe"
    for fmt in glb gltf dae; do
        mkdir -p "$base/app/converters/$fmt"
        cp "$base/app/converters/fbx/$exe" "$base/app/converters/$fmt/$exe"
    done

    # Strip debug symbols to shrink size
    find "$base/app" -name "*.pdb" -delete 2>/dev/null || true

    # Linux: bundle libdl.so (glibc 2.34+ merged libdl into libc; older .so refs still need it)
    if [[ "$rid" == linux* ]]; then
        local libdl
        libdl="$(ldconfig -p 2>/dev/null | awk '/libdl\.so\.2.*x86-64/{print $NF; exit}')"
        if [[ -f "$libdl" ]]; then
            cp "$libdl" "$base/app/libdl.so"
        fi
    fi

    # Create zip
    echo "  Zipping $name..."
    (cd "$OUTDIR" && zip -qr "${name}-v${VERSION}.zip" "$name/app")
    echo "  Done: $OUTDIR/${name}-v${VERSION}.zip"
}

# ── Linux ─────────────────────────────────────────────────────────────────────
build_gui "linux-x64" "XIVTools-linux"
assemble  "XIVTools-linux" "linux-x64" "converter"

# ── Windows ───────────────────────────────────────────────────────────────────
build_gui "win-x64" "XIVTools-windows"
assemble  "XIVTools-windows" "win-x64" "converter.exe"

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "Release packages ready:"
ls -lh "$OUTDIR"/*.zip
echo ""
echo "Linux:   $OUTDIR/XIVTools-linux-v${VERSION}.zip"
echo "Windows: $OUTDIR/XIVTools-windows-v${VERSION}.zip"
