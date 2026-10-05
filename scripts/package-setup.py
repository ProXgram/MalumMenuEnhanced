"""Package the single-file setup program with its corresponding repository source."""
from pathlib import Path, PurePosixPath
import argparse
import hashlib
import json
import shutil
import subprocess
import zipfile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--release", default="1.0", choices=("1.0", "1.0.1"), help="Release source archive version")
args = parser.parse_args()
published = root / "artifacts" / "setup" / "publish"
executable = published / "MalumMenuEnhancedSetup.exe"
if not executable.is_file():
    raise SystemExit("Build the setup program with the documented dotnet publish command first.")
if sorted(p.name for p in published.iterdir() if p.is_file()) != [executable.name]:
    raise SystemExit("The publish folder must contain exactly one executable, without extra dependency files.")
if executable.stat().st_size >= 100 * 1024 * 1024:
    raise SystemExit("The executable exceeds the repository download size limit.")
for name in ("installer.jpg", "menu.jpg"):
    if not (root / "docs" / "screenshots" / name).is_file():
        raise SystemExit("Capture the actual setup and mod screenshots before packaging.")
git_root = subprocess.check_output(["git", "-C", str(root), "rev-parse", "--show-toplevel"], text=True).strip()
if Path(git_root).resolve() != root:
    raise SystemExit("Package from this repository's own root.")
files = subprocess.check_output(["git", "-C", str(root), "ls-files", "--cached", "--others", "--exclude-standard", "-z"]).decode().split("\0")
source_files = sorted({name for name in files if name and not any(
    part.lower() in ("artifacts", "bin", "obj", ".git", "downloads") for part in PurePosixPath(name).parts
) and (root / name).is_file()})
release = root / "artifacts" / "setup" / "release"
release.mkdir(parents=True, exist_ok=True)
source_archive = release / f"MalumMenuEnhanced-{args.release}-Source.zip"
with zipfile.ZipFile(source_archive, "w", zipfile.ZIP_DEFLATED) as archive:
    for name in source_files:
        archive.write(root / name, name)
with zipfile.ZipFile(source_archive) as archive:
    assert archive.testzip() is None
    assert set(archive.namelist()) == set(source_files)
    assert all(archive.read(name) == (root / name).read_bytes() for name in source_files)
    assert archive.read("LICENSE") == (root / "LICENSE").read_bytes()
    assert "installer/MalumMenuEnhanced.Setup/SetupForm.cs" in archive.namelist()
    assert "installer/MalumMenuEnhanced.Setup.Core/InstallerService.cs" in archive.namelist()
downloads = root / "downloads"
(downloads / "setup").mkdir(parents=True, exist_ok=True)
shutil.copy2(executable, downloads / "setup" / executable.name)
source_destination = downloads / f"v{args.release}"
source_destination.mkdir(parents=True, exist_ok=True)
shutil.copy2(source_archive, source_destination / source_archive.name)
report = {
    "executable": executable.name,
    "installerRelease": args.release,
    "bytes": executable.stat().st_size,
    "sha256": hashlib.sha256(executable.read_bytes()).hexdigest().upper(),
    "selfContained": True,
    "singleFile": True,
    "sourceEntries": len(source_files),
    "sourceMatchesRepository": True,
    "loaderBundled": False,
    "screenshots": ["docs/screenshots/installer.jpg", "docs/screenshots/menu.jpg"],
}
verification = root / "artifacts" / "verification"
verification.mkdir(parents=True, exist_ok=True)
(verification / "setup-package.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report, indent=2))
