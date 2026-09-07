#!/usr/bin/env python3
"""Build the Godot pack that supplies Runmobile's mod-list icon."""

from __future__ import annotations

import hashlib
import re
import struct
import sys
from pathlib import Path

PACK_HEADER_MAGIC = 0x43504447  # GDPC
PACK_FORMAT_VERSION = 3
GODOT_VERSION = (4, 5, 1)
PACK_REL_FILEBASE = 1 << 1
ALIGNMENT = 32
ICON_PATH = "Runmobile/mod_image.png"
IMPORT_PATH = f"{ICON_PATH}.import"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
PNG_IHDR_LENGTH = 13
PNG_COLOR_TYPE_RGBA = 6


def usage() -> None:
    print(f"usage: {Path(sys.argv[0]).name} <asset directory> <output PCK>", file=sys.stderr)


def aligned(value: int, alignment: int) -> int:
    return value + (-value % alignment)


def icon_data(path: Path) -> bytes:
    data = path.read_bytes()
    if data[:8] != PNG_SIGNATURE:
        raise ValueError(f"{path} is not a PNG")
    if data[8:12] != struct.pack(">I", PNG_IHDR_LENGTH) or data[12:16] != b"IHDR":
        raise ValueError(f"{path} has no PNG IHDR")

    width, height, bit_depth, color_type = struct.unpack(">IIBB", data[16:26])
    if (width, height) != (64, 64):
        raise ValueError(f"{path} must be 64×64, not {width}×{height}")
    if bit_depth != 8 or color_type != PNG_COLOR_TYPE_RGBA:
        raise ValueError(f"{path} must be an 8-bit RGBA PNG")
    return data


def resources(asset_directory: Path) -> list[tuple[str, bytes]]:
    icon = icon_data(asset_directory / ICON_PATH)
    import_file = asset_directory / IMPORT_PATH
    import_data = import_file.read_bytes()
    imported_match = re.search(rb'^path="res://([^"\r\n]+)"$', import_data, re.MULTILINE)
    if imported_match is None:
        raise ValueError(f"{import_file} has no imported texture path")

    imported_path = imported_match.group(1).decode("utf-8")
    if not imported_path.startswith(".godot/imported/") or not imported_path.endswith(".ctex"):
        raise ValueError(f"{import_file} names an unsupported imported texture: {imported_path}")

    imported_data = (asset_directory / imported_path).read_bytes()
    return [(ICON_PATH, icon), (IMPORT_PATH, import_data), (imported_path, imported_data)]


def pack(asset_directory: Path, output: Path) -> None:
    files = resources(asset_directory)

    # PCKPacker's header is 104 bytes before its configured file alignment.
    file_base = aligned(104, ALIGNMENT)
    entries: list[tuple[str, bytes, int]] = []
    position = file_base
    for path, data in files:
        entries.append((path, data, position))
        position = aligned(position + len(data), ALIGNMENT)
    directory_offset = position

    header = struct.pack(
        "<6I2Q16I",
        PACK_HEADER_MAGIC,
        PACK_FORMAT_VERSION,
        *GODOT_VERSION,
        PACK_REL_FILEBASE,
        file_base,
        directory_offset,
        *([0] * 16),
    )

    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix(output.suffix + ".tmp")
    try:
        with temporary.open("wb") as stream:
            stream.write(header)
            stream.write(b"\0" * (file_base - stream.tell()))
            for path, data, offset in entries:
                stream.write(b"\0" * (offset - stream.tell()))
                stream.write(data)
            stream.write(b"\0" * (directory_offset - stream.tell()))
            stream.write(struct.pack("<I", len(entries)))
            for path, data, offset in entries:
                encoded_path = path.encode("utf-8")
                padded_path_length = aligned(len(encoded_path), 4)
                stream.write(struct.pack("<I", padded_path_length))
                stream.write(encoded_path.ljust(padded_path_length, b"\0"))
                stream.write(struct.pack("<QQ", offset - file_base, len(data)))
                stream.write(hashlib.md5(data).digest())
                stream.write(struct.pack("<I", 0))
        temporary.replace(output)
    finally:
        temporary.unlink(missing_ok=True)


def main() -> int:
    if len(sys.argv) != 3:
        usage()
        return 2

    try:
        pack(Path(sys.argv[1]), Path(sys.argv[2]))
    except (OSError, UnicodeDecodeError, ValueError) as error:
        print(f"Could not pack Runmobile's icon: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
