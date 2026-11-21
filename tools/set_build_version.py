"""Stamp the same release number into Godot, Android and the browser bundle."""

import argparse
import json
from pathlib import Path
import re


def replace_setting(path, key, value):
    text = path.read_text(encoding="utf-8")
    text, matches = re.subn(rf"(?m)^{re.escape(key)}=.*$", f"{key}={value}", text)
    if matches != 1:
        raise ValueError(f"expected one {key} entry in {path}, got {matches}")
    path.write_text(text, encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run_number", type=int)
    args = parser.parse_args()
    if not 0 <= args.run_number < 2_099_999_000:
        parser.error("run number is outside the Android version-code range")
    version = f"0.1.{args.run_number}"
    replace_setting(Path("project.godot"), "config/version", json.dumps(version))
    preset = Path("export_presets.cfg")
    replace_setting(preset, "version/code", args.run_number + 1000)
    replace_setting(preset, "version/name", json.dumps(version))
    Path("Dizzy.web/wwwroot/version.json").write_text(json.dumps({"version": version}) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
