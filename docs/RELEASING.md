# Preparing a release

The repository contains source code, original application assets, Lucide SVGs with their notices, tests and build tools. Binary downloads are generated locally and excluded from Git.

## Build inputs

A source-only build needs Windows, .NET Framework 4.8 and its existing C# compiler:

```powershell
.\build.ps1 -SourceOnly -Verify -PortableName SourceBuild
```

A complete package additionally needs authorized native codec inputs. Their exact expected hashes and relative destinations are in `tools/codec-package.json`. Place the codec EXEs and PSP runtime in a private directory with ATRAC and LAME subfolders, then pass it with -CodecSourceDirectory. The default private input directory is .analysis/codec-inputs.

Pass matching app-local runtime files with -RuntimeDirectory; its default is .analysis/runtimes/ready. Each catalog entry with a Source field specifies the path relative to that directory. Microsoft VC80, VC90 and VC120 x86 files must come from verified official packages, retain their manifests/notices and match the catalog. Native executable bytes must not be modified to change their identity.

The runtime data-inspection utilities in tools read cabinets and MSI streams without installing packages or executing their custom actions. They are development tools, not automatic downloaders. No Python, dependency installation, system runtime registration or credential modification is performed by the build.

```powershell
.\build.ps1 -Verify -Package -PortableName Release `
  -CodecSourceDirectory '<authorized codec input folder>' `
  -RuntimeDirectory '<authorized app-local runtime folder>'
```

Packaging additionally requires an authorized, officially signed Inno Setup 6.7.3 compiler. Pass its ISCC.exe path with -InnoCompiler. The default project-local path is .analysis/inno/compiler/ISCC.exe. Build scripts never download or install the compiler automatically. The compiler can be installed using its official /PORTABLE=1 mode, which avoids registration and shortcuts.

The builder verifies codec hashes, compiles the application, runs the C# checks, creates the portable archive and builds the classic Inno Setup wizard. Verification installs into a new project test folder and compares every installed file against the portable inventory by SHA-256. It also checks that the installer refuses a nonempty destination without altering its contents. The generated installer creates no registry entries or shortcuts and does not install system runtimes.

To rebuild only the installer from an existing portable ZIP, preserving the archive's bytes:

```powershell
.\tools\BuildClassicInstaller.ps1 -Verify -UpdateRelease `
  -InnoCompiler '<authorized Inno Setup folder>\ISCC.exe'
```

This replaces only the local setup EXE and SHA256SUMS.txt. It does not push or upload anything. The C# helper in tools/Setup.cs generates the approved artwork; it is not an installer implementation.

The separate -VerifyNative option executes the actual codecs on generated audio. It returns failure if a platform fails and is never treated as equivalent to the synthetic wrapper checks. Run native binaries only in an environment you have authorized for that purpose.

## Installer folder

Release output contains exactly:

- PS_Converter-v1.0-setup.exe
- PS_Converter-v1.0-portable.zip
- SHA256SUMS.txt
- RELEASE-v1.0.txt

-CleanInstaller explicitly removes previous contents of this project's installer folder after new packages are built and verified. It rejects unexpected targets and reparse points. Use that switch only when you intend to delete those generated files; close any running application from the old folder first. Normal package builds replace only the four known output filenames.

Never commit the installer folder, codec/runtime binaries, downloaded archives, analysis records, settings, caches, test audio or signing credentials. Run tools/CheckPublicFiles.ps1 against the staged inventory before committing.

## Publication

MIT covers PS_Converter source code, not the native ATRAC tools or Microsoft runtimes. Native codec redistribution rights remain unresolved. **Confirm applicable third-party redistribution terms before uploading binary release assets.** Generated installer/ZIP files are local preparation artifacts; their existence is not licensing clearance.

Push the reviewed source commits yourself:

```powershell
git push -u origin main
git push origin v1.0
```

To create a GitHub release after the source is pushed, choose Releases → Draft a new release, select tag v1.0, use the English text in installer/RELEASE-v1.0.txt, and attach the approved release files. Release artifacts are uploaded separately; Git ignores them.

Before sharing a package, recompute its SHA-256 value and compare it with SHA256SUMS.txt. No push, GitHub release creation or binary upload is performed by the local build scripts.
