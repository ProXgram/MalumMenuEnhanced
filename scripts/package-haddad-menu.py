"""Package the plugin and matching source; optionally include a Windows BepInEx loader."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import shutil
import subprocess
import zipfile
import argparse
from datetime import date

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--version", default="1.0")
parser.add_argument("--dll", type=Path, help="Built DLL; defaults to src/bin/Release/net6.0/MalumMenuEnhanced.dll, with the current isolated build as a fallback.")
parser.add_argument("--loader-package", type=Path, help="Compatible Windows 64-bit BepInEx 6 IL2CPP ZIP to include in the full Windows package. Without this, produce Plugin.zip for an existing BepInEx installation.")
args = parser.parse_args()
version = args.version
normal_build = root / "src" / "bin" / "Release" / "net6.0" / "MalumMenuEnhanced.dll"
isolated_build = root / "artifacts" / "build" / version / "bin" / "MalumMenuEnhanced.dll"
built = args.dll.expanduser().resolve() if args.dll else (normal_build if normal_build.is_file() else isolated_build)
if not built.is_file():
    parser.error("Built DLL not found. Run the documented Release build first, or supply --dll.")
baseline = root / "artifacts" / "release" / "v3.3.6-au19" / "MalumMenu-3.3.6-au19-MicrosoftStore-EpicGames-XboxApp.zip"
loader_package = args.loader_package.expanduser().resolve() if args.loader_package else None
if loader_package and not loader_package.is_file():
    parser.error("The supplied --loader-package ZIP was not found.")
git_root = subprocess.run(["git", "-C", str(root), "rev-parse", "--show-toplevel"], capture_output=True, text=True)
if git_root.returncode or Path(git_root.stdout.strip()).resolve() != root:
    parser.error("Initialize this source directory as its own Git repository before packaging the matching source.")
git_files = subprocess.run(["git", "-C", str(root), "ls-files", "--cached", "--others", "--exclude-standard", "-z"], check=True, capture_output=True).stdout.decode("utf-8").split("\0")
release = root / "artifacts" / "release" / ("v" + version)
package_suffix = "MicrosoftStore-EpicGames-XboxApp" if loader_package else "Plugin"
binary_zip = release / ("MalumMenuEnhanced-" + version + "-" + package_suffix + ".zip")
source_zip = release / ("MalumMenuEnhanced-" + version + "-Source.zip")
if binary_zip.exists() or source_zip.exists():
    raise SystemExit("Release archives already exist; review them before repackaging.")
binary = built.read_bytes()
binary_hash = hashlib.sha256(binary).hexdigest().upper()
docs = ("README.md", "FORK.md", "CREDITS.md", "FEATURES.md", "LICENSE")
old_entry = "BepInEx/plugins/MalumMenu.dll"
new_entry = "BepInEx/plugins/MalumMenuEnhanced.dll"
menu_entries = {"bepinex/plugins/" + name for name in ("malummenu.dll", "haddadmenu.dll", "malummenuenhanced.dll")}
if loader_package:
    with zipfile.ZipFile(loader_package) as original:
        loader_entries = {name.replace("\\", "/").lower() for name in original.namelist()}
        if "winhttp.dll" not in loader_entries or "bepinex/core/bepinex.unity.il2cpp.dll" not in loader_entries:
            parser.error("The loader ZIP must contain a compatible Windows BepInEx IL2CPP loader at its root.")
        if loader_package.name == baseline.name:
            assert hashlib.sha256(original.read(old_entry)).hexdigest().upper() == "044640452250D1CAF9F2016CA044F01D0A86F1DBDAE1DA407F3D401730983583", "Baseline package changed."
        if original.testzip() is not None:
            parser.error("The supplied loader ZIP failed its integrity check.")
release.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(binary_zip, "x", zipfile.ZIP_DEFLATED) as archive:
    if loader_package:
        with zipfile.ZipFile(loader_package) as original:
            for item in original.infolist():
                if item.filename.replace("\\", "/").lower() in menu_entries or item.filename in docs:
                    continue
                archive.writestr(item, original.read(item.filename))
    archive.writestr(new_entry, binary)
    for document in docs:
        archive.write(root / document, document)
with zipfile.ZipFile(binary_zip) as archive:
    entries = archive.namelist()
    assert old_entry not in entries
    assert len([name for name in entries if name.startswith("BepInEx/plugins/") and name.endswith(".dll")]) == 1
    assert archive.read(new_entry) == binary
    assert archive.read("LICENSE") == (root / "LICENSE").read_bytes()
    assert archive.testzip() is None
    binary_count = len(entries)
source_files = []
with zipfile.ZipFile(source_zip, "x", zipfile.ZIP_DEFLATED) as archive:
    for name in sorted(set(git_files)):
        if not name or any(part.lower() in ("artifacts", "bin", "obj", ".git", "downloads") for part in PurePosixPath(name).parts):
            continue
        file = root / name
        if file.is_file():
            archive.write(file, name)
            source_files.append(name)
with zipfile.ZipFile(source_zip) as archive:
    assert archive.read("src/ModBranding.cs") == (root / "src" / "ModBranding.cs").read_bytes()
    assert archive.read("LICENSE") == (root / "LICENSE").read_bytes()
    assert "CREDITS.md" in archive.namelist()
    assert archive.testzip() is None
shutil.copy2(built, release / "MalumMenuEnhanced.dll")
report = {
    "localDate": date.today().isoformat(), "name": "MalumMenu Enhanced", "creator": "Rifegul", "version": version,
    "dllSha256": binary_hash, "binaryPackage": str(binary_zip), "binaryEntries": binary_count,
    "packageKind": "full-windows" if loader_package else "plugin-only",
    "requiresExistingBepInEx": loader_package is None,
    "loaderPackage": str(loader_package) if loader_package else None,
    "sourcePackage": str(source_zip), "sourceEntries": len(source_files),
    "originalLicenseUnchanged": True, "originalAuthorsCredited": True,
    "oldPluginEntryAbsent": True, "oneMenuPluginInPackage": True,
    "builtDllMatchesPackagedDll": True, "sourceIncludesLocalChanges": True,
    "installed": False, "startupVerified": False,
}
verification = root / "artifacts" / "verification"
verification.mkdir(parents=True, exist_ok=True)
(verification / ("malummenu-enhanced-" + version.removesuffix("-au19") + ".json")).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report, indent=2))
