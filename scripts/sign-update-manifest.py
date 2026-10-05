"""Sign the latest compatible MalumMenu Enhanced release with a publisher key.

Requires Python and the cryptography package. Build the plugin ZIP and setup
program first, then supply --version, --private-key and optional repeated
--game-version values. Store the private key outside this repository; it is
never included in the manifest. --dry-run verifies and signs without writing.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import tempfile

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa


ROOT = Path(__file__).resolve().parents[1]
MAX_ASSET_BYTES = 100 * 1024 * 1024
MAX_PAYLOAD_BYTES = 32 * 1024
MAX_ENVELOPE_BYTES = 64 * 1024


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def numeric_version(value: str) -> str:
    parts = value.split(".")
    if not (2 <= len(parts) <= 4 and len(value) <= 43 and all(
        re.fullmatch(r"0|[1-9][0-9]*", part) and int(part) <= 2147483647 for part in parts
    )):
        raise argparse.ArgumentTypeError("Version must contain two to four numeric parts, without prerelease labels.")
    return value


def release_asset(path: Path, version: str) -> dict[str, str | int]:
    require(path.is_file() and not path.is_symlink() and ROOT in path.resolve().parents,
            "A normal release asset inside this repository is required.")
    size = path.stat().st_size
    require(0 < size < MAX_ASSET_BYTES, "A release asset exceeds the manifest size limit.")
    sha = hashlib.sha256()
    bytes_read = 0
    with path.open("rb") as source:
        while block := source.read(1024 * 1024):
            bytes_read += len(block)
            require(bytes_read <= size, "A release asset changed while it was being read.")
            sha.update(block)
    require(bytes_read == size, "A release asset changed while it was being read.")
    return {
        "url": f"https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v{version}/{path.name}",
        "sha256": sha.hexdigest().upper(),
        "size": size,
    }


def publisher_key(private_path: Path) -> rsa.RSAPrivateKey:
    require(private_path.is_file() and not private_path.is_symlink(), "A normal publisher private-key file is required.")
    resolved = private_path.resolve()
    require(resolved != ROOT and ROOT not in resolved.parents,
            "Keep the publisher private key outside the repository.")
    try:
        size = private_path.stat().st_size
        require(0 < size <= 32 * 1024, "The publisher key file size is invalid.")
        private_bytes = private_path.read_bytes()
        require(len(private_bytes) == size, "The publisher key file changed while it was being read.")
    except OSError:
        raise SystemExit("The publisher private-key file could not be read.") from None
    try:
        private_key = serialization.load_pem_private_key(private_bytes, password=None)
    except (TypeError, ValueError):
        raise SystemExit("The publisher key must be a valid unencrypted RSA PEM private key.") from None
    require(isinstance(private_key, rsa.RSAPrivateKey) and 2048 <= private_key.key_size <= 8192,
            "The publisher key must be RSA with a supported size.")
    trust = ROOT / "src/Updates/UpdateTrust.cs"
    require(trust.is_file() and not trust.is_symlink(), "The embedded publisher public key is missing.")
    match = re.search(r"-----BEGIN PUBLIC KEY-----[\s\S]*?-----END PUBLIC KEY-----", trust.read_text(encoding="utf-8-sig"))
    require(match is not None, "The embedded publisher public key is missing.")
    try:
        public_key = serialization.load_pem_public_key(match.group(0).encode("ascii"))
    except (TypeError, ValueError, UnicodeError):
        raise SystemExit("The embedded publisher public key is invalid.") from None
    require(isinstance(public_key, rsa.RSAPublicKey), "The embedded publisher public key must be RSA.")
    require(private_key.public_key().public_numbers() == public_key.public_numbers(),
            "The publisher private key does not match the key embedded in the mod and installer.")
    return private_key


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True, type=numeric_version, help="Matching numeric mod version and GitHub release tag.")
    parser.add_argument("--private-key", required=True, type=Path, help="Publisher RSA PEM private key stored outside this repository.")
    parser.add_argument("--game-version", action="append", type=numeric_version, help="Supported Among Us version; repeat for multiple versions.")
    parser.add_argument("--dry-run", action="store_true", help="Verify and sign without replacing updates/latest.json.")
    args = parser.parse_args()
    games = args.game_version or ["2026.9.29"]
    require(1 <= len(games) <= 16 and len(set(games)) == len(games), "Supply one to sixteen distinct supported game versions.")
    plugin = ROOT / f"downloads/v{args.version}/MalumMenuEnhanced-{args.version}-Plugin.zip"
    setup = ROOT / "downloads/setup/MalumMenuEnhancedSetup.exe"
    payload = {
        "product": "MalumMenuEnhanced",
        "modVersion": args.version,
        "supportedGameVersions": games,
        "pluginZip": release_asset(plugin, args.version),
        "setupExe": release_asset(setup, args.version),
    }
    payload_bytes = json.dumps(payload, ensure_ascii=False, separators=(",", ":"), sort_keys=True).encode("utf-8")
    require(0 < len(payload_bytes) <= MAX_PAYLOAD_BYTES, "The manifest payload exceeds its byte limit.")
    key = publisher_key(args.private_key)
    signature = key.sign(payload_bytes, padding.PKCS1v15(), hashes.SHA256())
    key.public_key().verify(signature, payload_bytes, padding.PKCS1v15(), hashes.SHA256())
    envelope = {
        "payload": base64.b64encode(payload_bytes).decode("ascii"),
        "signature": base64.b64encode(signature).decode("ascii"),
    }
    data = (json.dumps(envelope, ensure_ascii=True, separators=(",", ":"), sort_keys=True) + "\n").encode("utf-8")
    require(0 < len(data) <= MAX_ENVELOPE_BYTES, "The manifest envelope exceeds its byte limit.")
    output = ROOT / "updates/latest.json"
    if not args.dry_run:
        output.parent.mkdir(parents=True, exist_ok=True)
        require(not output.parent.is_symlink() and ROOT in output.parent.resolve().parents and not output.is_symlink(),
                "The manifest output must be inside the repository and must not be linked.")
        temporary_path = None
        try:
            with tempfile.NamedTemporaryFile(prefix=".latest-", suffix=".tmp", dir=output.parent, delete=False) as temporary:
                temporary_path = Path(temporary.name)
                temporary.write(data)
            temporary_path.replace(output)
        finally:
            if temporary_path is not None and temporary_path.exists():
                temporary_path.unlink()
    print(json.dumps({
        "manifest": output.relative_to(ROOT).as_posix(),
        "modVersion": args.version,
        "supportedGameVersions": games,
        "signatureVerified": True,
        "bytes": len(data),
        "sha256": hashlib.sha256(data).hexdigest().upper(),
        "dryRun": args.dry_run,
    }, indent=2))


if __name__ == "__main__":
    try:
        main()
    except OSError:
        raise SystemExit("A release input or output could not be accessed.") from None
