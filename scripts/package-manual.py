"""Build and verify the complete manual Windows PC package from pinned inputs.

Run from any folder: python scripts/package-manual.py
The optional --draft mode creates an explicitly unfinished preview while
redistribution notices and corresponding-source assets are being collected.
Existing plugin/source/setup releases and the installed game are never changed.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import platform
import re
import stat
import tempfile
import zipfile
import zlib


ROOT = Path(__file__).resolve().parents[1]
LOADER = ROOT / "artifacts/verification/downloader-inputs/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip"
PLUGIN = ROOT / "downloads/v1.0/MalumMenuEnhanced-1.0-Plugin.zip"
LOADER_SHA = "3616D6A67F5F595973EC4AA7BD7EDAF7F799D5BB9926F7146A6DCC7B4ABF478F"
PLUGIN_SHA = "227F8D82300F5F89D30C49BEEB4E83C152C072D07BAC5400B194D0D6FBB6B8C1"
DLL_SHA = "D3FCC31C69C83F0381F25B5AC33FF052162D9BDCD6D74CDBDEFBFFEDD6E22559"
DLL_PATH = "BepInEx/plugins/MalumMenuEnhanced.dll"
OUTPUT_NAME = "MalumMenuEnhanced-1.0-MicrosoftStore-EpicGames-XboxApp.zip"
LEGAL = ROOT / "installer/ManualLegal"
TIMESTAMP = (2026, 10, 5, 0, 0, 0)
MAX_FILE = 128 * 1024 * 1024
MAX_TOTAL = 512 * 1024 * 1024

INSTALL = """MalumMenu Enhanced 1.0 by Rifegul
Windows PC: Microsoft Store / Epic Games / Xbox App
Among Us 19.0.0 / 2026.9.29

1. Close Among Us.
2. Extract this ZIP, then copy everything inside it into the game folder containing Among Us.exe. Copy BepInEx, dotnet and the loose files directly beside Among Us.exe, not the outer ZIP folder.
3. Open Among Us from your usual launcher. Wait for the first launch to finish preparing the mod.
4. Press Delete to open the menu.

Microsoft Store / Xbox App: Xbox App > Among Us > Manage > Files > Browse > Content.
Epic Games: Library > Among Us > Manage > Installation > folder icon.

Updating another menu? Back up old MalumMenu.dll, HaddadMenu.dll or MalumMenuEnhanced.dll outside BepInEx/plugins first. Keep only one menu DLL active.

The Xbox / Microsoft Store build has been tested. Epic Games uses this same manual package but has not yet been tested. This package is for PC, not Xbox consoles.

Source and credits: https://github.com/ProXgram/MalumMenuEnhanced
"""


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def safe_name(name: str) -> str:
    require(name and "\\" not in name and not name.startswith("/"), "Unsafe package path.")
    normalized = name.rstrip("/")
    require(normalized and len(normalized) <= 240, "Invalid package path length.")
    for part in normalized.split("/"):
        require(part not in ("", ".", "..") and not part.endswith((".", " ")) and
                not any(ord(c) < 32 or c in '<>:"|?*' for c in part), "Unsafe package path.")
        stem = part.split(".")[0].upper()
        require(stem not in ("CON", "PRN", "AUX", "NUL") and
                not re.fullmatch(r"(?:COM|LPT)[1-9¹²³]", stem), "Reserved package file name.")
    return normalized


def pinned_zip(path: Path, expected_hash: str) -> tuple[dict[str, bytes], set[str]]:
    require(path.is_file() and not path.is_symlink(), "A verified release input is missing.")
    require(digest(path.read_bytes()) == expected_hash, "A release input does not match its pinned SHA256.")
    files: dict[str, bytes] = {}
    directories: set[str] = set()
    names: dict[str, bool] = {}
    total = 0
    with zipfile.ZipFile(path) as archive:
        require(len(archive.infolist()) <= 4096, "Too many archive entries.")
        for entry in archive.infolist():
            name = safe_name(entry.filename)
            is_directory = entry.is_dir()
            require(name.lower() not in names, "Duplicate archive destination.")
            names[name.lower()] = is_directory
            mode = entry.external_attr >> 16
            require(not stat.S_ISLNK(mode) and not entry.external_attr & 0x400,
                    "Linked archive entries are not supported.")
            total += entry.file_size
            require(0 <= entry.file_size <= MAX_FILE and total <= MAX_TOTAL, "Archive size exceeds safe limits.")
            if is_directory:
                require(entry.file_size == 0, "A directory contains unexpected data.")
                directories.add(name)
            else:
                data = archive.read(entry)
                require(len(data) == entry.file_size, "An archive file is incomplete.")
                files[name] = data
        require(archive.testzip() is None, "Archive CRC verification failed.")
    for name in names:
        parts = name.split("/")
        for i in range(1, len(parts)):
            ancestor = "/".join(parts[:i])
            require(names.get(ancestor, True), "An archive file blocks a child directory.")
    return files, directories


def legal_assets(draft: bool, loader: dict[str, bytes]) -> tuple[dict[str, bytes], bool]:
    manifest_path = LEGAL / "inventory.json"
    if not manifest_path.is_file():
        require(draft, "Required redistribution inventory is not ready. Use --draft for an unfinished preview.")
        return {}, False
    require(not manifest_path.is_symlink(), "The redistribution inventory must be a normal file.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    complete = manifest.get("complete") is True
    require(draft or complete, "Redistribution inventory is unfinished. Use --draft for a preview.")
    if complete:
        loader_inventory = manifest.get("loader_files")
        require(isinstance(loader_inventory, list) and len(loader_inventory) == len(loader),
                "Redistribution inventory does not cover every loader file.")
        indexed = {safe_name(entry["path"]): entry for entry in loader_inventory}
        require(set(indexed) == set(loader), "Redistribution inventory has missing or duplicate loader paths.")
        for name, data in loader.items():
            require(indexed[name]["sha256"].upper() == digest(data),
                    "A redistribution inventory hash differs from the pinned loader.")
        require(manifest.get("components"), "Redistribution component/source inventory is missing.")
    entries = manifest.get("files")
    require(isinstance(entries, list) and entries, "The redistribution inventory is empty.")
    assets = {"ThirdParty/inventory.json": manifest_path.read_bytes()}
    listed = set()
    for entry in entries:
        relative = safe_name(entry["path"])
        require(relative.lower() not in listed and relative != "inventory.json", "Duplicate redistribution asset.")
        listed.add(relative.lower())
        path = LEGAL.joinpath(*relative.split("/"))
        require(path.is_file() and not path.is_symlink() and
                LEGAL.resolve() in path.resolve().parents, "A redistribution asset is missing or linked.")
        data = path.read_bytes()
        require(digest(data) == entry["sha256"].upper(), "A redistribution asset differs from its audited hash.")
        require(len(data) <= MAX_FILE, "A redistribution asset is too large.")
        assets["ThirdParty/" + relative] = data
    actual = {p.relative_to(LEGAL).as_posix().lower() for p in LEGAL.rglob("*") if p.is_file()}
    require(actual == listed | {"inventory.json"}, "Redistribution folder contains unaudited files.")
    return assets, complete


def check_release_text(name: str, data: bytes) -> None:
    # Encoded markers keep build-environment names out of published source text.
    markers = [bytes.fromhex(v) for v in ("63686174677074", "636f646578", "6f70656e6169")]
    lower = data.lower()
    for marker in markers:
        require(marker not in lower and marker.decode().encode("utf-16le") not in lower,
                "Release content contains an unwanted build-environment reference: " + name)
    require(b"c:\\users\\" not in lower and b"c:/users/" not in lower,
            "Release content contains an absolute workspace path: " + name)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--draft", action="store_true", help="Build an explicitly unfinished preview.")
    args = parser.parse_args()
    loader, directories = pinned_zip(LOADER, LOADER_SHA)
    plugin, _ = pinned_zip(PLUGIN, PLUGIN_SHA)
    require(len(loader) == 228, "The loader file count differs from the verified release.")
    required_loader = {"winhttp.dll", "doorstop_config.ini", ".doorstop_version",
                       "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "dotnet/coreclr.dll"}
    require(required_loader <= loader.keys(), "The verified loader layout is incomplete.")
    expected_plugin = {DLL_PATH, "README.md", "FORK.md", "CREDITS.md", "FEATURES.md", "LICENSE"}
    require(set(plugin) == expected_plugin and digest(plugin[DLL_PATH]) == DLL_SHA,
            "The plugin archive differs from its verified release layout.")
    require(DLL_PATH not in loader, "The loader already contains a menu DLL.")
    files = dict(loader)
    files[DLL_PATH] = plugin[DLL_PATH]
    for name in ("LICENSE", "CREDITS.md"):
        path = ROOT / name
        require(path.is_file() and not path.is_symlink(), "A root license or credits file is missing.")
        files[name] = path.read_bytes()
    files["INSTALL.txt"] = INSTALL.replace("\n", "\r\n").encode("utf-8")
    assets, legal_complete = legal_assets(args.draft, loader)
    require(not set(files) & set(assets), "A notice asset would overwrite a loader file.")
    files.update(assets)
    if args.draft:
        files["DRAFT.txt"] = b"UNFINISHED PREVIEW. Redistribution notices are not yet approved. Do not distribute.\r\n"

    # Official third-party files remain byte-for-byte unchanged, including notices.
    for name in (DLL_PATH, "LICENSE", "CREDITS.md", "INSTALL.txt"):
        check_release_text(name, files[name])
    require([n for n in files if n.lower().startswith("bepinex/plugins/") and n.lower().endswith(".dll")] == [DLL_PATH],
            "The package must contain exactly one active menu DLL.")
    require(not any(n.lower().endswith(("among us.exe", "gameassembly.dll", "unityplayer.dll")) or
                    n.lower().startswith("among us_data/") for n in files), "Game files must not be redistributed.")
    names = {safe_name(n).lower() for n in files}
    require(len(names) == len(files), "Case-insensitive package collision.")
    require(sum(len(v) for v in files.values()) <= MAX_TOTAL, "The complete package exceeds its safe size limit.")

    output_directory = ROOT / ("artifacts/verification/manual-package" if args.draft else "downloads/manual")
    output_directory.mkdir(parents=True, exist_ok=True)
    output = output_directory / OUTPUT_NAME.replace(".zip", "-DRAFT.zip") if args.draft else output_directory / OUTPUT_NAME
    with tempfile.TemporaryDirectory(prefix="MenuManualPackage-") as temporary:
        staging_zip = Path(temporary) / output.name
        with zipfile.ZipFile(staging_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for name in sorted(directories):
                entry = zipfile.ZipInfo(name + "/", TIMESTAMP)
                entry.create_system = 3
                entry.external_attr = (stat.S_IFDIR | 0o755) << 16 | 0x10
                archive.writestr(entry, b"")
            for name, data in sorted(files.items()):
                entry = zipfile.ZipInfo(name, TIMESTAMP)
                entry.create_system = 3
                entry.external_attr = (stat.S_IFREG | 0o644) << 16
                entry.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(entry, data, compresslevel=9)
        with zipfile.ZipFile(staging_zip) as archive:
            require(archive.testzip() is None, "The output archive failed CRC validation.")
            require({i.filename for i in archive.infolist() if not i.is_dir()} == set(files), "The output layout differs from the plan.")
            for name, data in files.items():
                require(archive.read(name) == data, "A packaged file differs from its verified bytes.")
            extraction = Path(temporary) / "extracted"
            archive.extractall(extraction)
            require((extraction / "Among Us.exe").exists() is False and
                    (extraction / DLL_PATH).is_file(), "The extracted package layout is invalid.")
            for name, data in loader.items():
                require((extraction / name).read_bytes() == data, "An extracted official loader file changed.")
        require(staging_zip.stat().st_size < 100 * 1024 * 1024, "The archive exceeds the repository download limit.")
        # Only this script's newly generated manual output is replaced.
        output.write_bytes(staging_zip.read_bytes())

    report = {
        "package": output.relative_to(ROOT).as_posix(),
        "draft": args.draft,
        "bytes": output.stat().st_size,
        "sha256": digest(output.read_bytes()),
        "loaderArchiveSha256": LOADER_SHA,
        "pluginArchiveSha256": PLUGIN_SHA,
        "pluginDllSha256": DLL_SHA,
        "officialLoaderFiles": len(loader),
        "allOfficialLoaderBytesPreserved": True,
        "menuDllCount": 1,
        "gameFilesBundled": False,
        "safePaths": True,
        "crcVerified": True,
        "extractedLayoutVerified": True,
        "redistributionInventoryComplete": legal_complete,
        "redistributionLoaderInventoryVerified": legal_complete,
        "thirdPartyAssetCount": len(assets),
        "python": platform.python_version(),
        "zlib": zlib.ZLIB_VERSION,
        "fixedZipTimestamp": list(TIMESTAMP),
        "epicGamesLiveTested": False,
    }
    verification = ROOT / "artifacts/verification"
    verification.mkdir(parents=True, exist_ok=True)
    (verification / ("manual-package-draft.json" if args.draft else "manual-package.json")).write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
