#!/usr/bin/env bash
set -euo pipefail

# Native revision used by 2dog 4.7.2.152 / browser-wasm 4.7.2.62.
revision=7606ec3bdc344067f6e55916cdd203324c011832
output=$(realpath -m "${1:?provide the output directory for TwoDogWebNativeDir}")
source_dir=$(mktemp -d)
trap 'rm -rf "$source_dir"' EXIT
git -C "$source_dir" init -q
git -C "$source_dir" fetch -q --depth 1 https://github.com/thygrrr/godot.git "$revision"
git -C "$source_dir" checkout -q --detach FETCH_HEAD

cd "$source_dir"
scons -j"$(nproc)" platform=web arch=wasm32 target=template_release \
    library_type=static_library extra_suffix=static_library threads=no \
    lto=none disable_crash_handler=yes disable_path_overrides=no optimize=size_extra \
    webgpu=no disable_3d=yes modules_enabled_by_default=no \
    module_mono_enabled=yes module_freetype_enabled=yes module_text_server_fb_enabled=yes

# Use the JS glue, worklets and boot script produced alongside this exact archive.
mkdir -p "$output/shell"
cp -R bin/.web_zip/libgodot "$output/"
cp bin/.web_zip/{godot.js,godot.audio.worklet.js,godot.audio.position.worklet.js} "$output/shell/"
test -s "$output/libgodot/libgodot.a"
