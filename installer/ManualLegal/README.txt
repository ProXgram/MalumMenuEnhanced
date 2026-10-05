Third-party software in the complete Windows manual package
==========================================================

MalumMenu Enhanced uses the unmodified BepInEx Unity IL2CPP Windows x64
loader build 755. The loader and its dependencies remain under their own
licenses. The root GPL-3.0 license applies to MalumMenu Enhanced; it does
not replace the third-party notices in this directory.

Full upstream license and notice files are preserved byte-for-byte in
component folders. The accompanying provenance records identify their
primary source URLs, revisions and SHA256 checksums. No loader binary
is modified by the manual-package builder.

BepInEx and Unity Doorstop use LGPL2.1; Il2CppInterop uses LGPL3. Their
matching, unmodified source archives and build scripts are included in
Sources/ and InteropAndDetours/ as listed in inventory.json.
The archives contain the preferred source form; the published loader
binaries can be replaced with compatible modified builds. No restriction
on modification or reverse engineering for debugging such modifications
is added by this package. Follow each project's included build guidance
and preserve its notices when redistributing a modified copy.

Most other managed dependencies use MIT; native Dobby uses Apache2.0.
The native Capstone runtimes use BSD3, with their notice preserved too.
The .NET6.0.7 runtime and its embedded third-party code have separate
notices in DotNetRuntime/. Both the original runtime6.0.7 notices and the
BepInEx runtime release-tag notices are preserved. The larger, newer
.NET runtime used by the optional setup EXE is not in this manual ZIP.

The component license texts, their warranties and limitations govern
these components. The accompanying source archives are distributed
unchanged, including their original authors and licenses.

Official loader:
https://builds.bepinex.dev/projects/bepinex_be/755
Loader ZIP SHA256:
3616D6A67F5F595973EC4AA7BD7EDAF7F799D5BB9926F7146A6DCC7B4ABF478F

Matching mod source is separately included in the repository download:
downloads/v1.0/MalumMenuEnhanced-1.0-Source.zip
https://github.com/ProXgram/MalumMenuEnhanced
