# Verification record

## v1.0 presentation and packaging — 2026-10-16

The requested visual update is complete. All ten conversion/core source files were compared against a SHA-256 baseline and remain byte-for-byte unchanged. Only native presentation, icon assets, release metadata, developer tooling, documentation and packaging were changed.

### Application checks

The complete C# suite passed 31 checks with warnings treated as errors. It covers console profiles and codec arguments, Windows decoding/resampling, generated MP3 input, output validation, collision preservation, process quoting/cancellation/deadlines, configuration, eight wrapper routes using synthetic codecs, native UI rendering, format inference, PSP default bitrate, missing-package behavior, direct conversion, codec hashes, relocation and tampering.

Two presentation regressions were reproduced before the fix: off-center Convert and incomplete Options icon/control presentation. The final layout checks pass at widths 730, 760 and 1024 pixels. The visible playback controls are absent; the long file row stays below Add files; Options retains 128 kbps for PS3 with an application icon and accessible control name. Rendered native windows were visually inspected. Combo fields have visible dark outlines, file panels have square corners and the app mark is large with transparent outer corners.

The explicit source-only build passed 28 synthetic checks and clearly skipped the three tests requiring real codec inventory files. It builds without native codec downloads or a private reference checkout. That mode is not a complete conversion release package.

### Native codec observations

The separate native suite was rerun with generated stereo tones and silent MP3 frames:

| Platform | Result in the command sandbox |
|---|---|
| PS Vita | WAV/MP3 → AT9 → WAV/MP3 passed; recovered tone duration and nonzero RMS checked |
| PS4 | WAV/MP3 → AT9 → WAV/MP3 passed with the correct app-local x86 runtime |
| Vita Convert button | Clean settings produced a real AT9 file without an executable picker |
| PSP | Pre-existing Windows DLL relocation loader failure |
| PS3 | Pre-existing Windows DLL relocation loader failure |

The native suite returned failure for the two blocked platform groups. That failure is reported separately from the 31 passing wrapper/presentation checks. The private report is retained in the ignored analysis folder. The user reported working conversion in their ordinary Windows session; physical consoles and a pristine Windows installation were not tested here.

The earlier automatic permission review rejected executing the unsigned legacy encoders outside the sandbox. That restriction was not bypassed. No new unsandboxed codec execution was performed for this visual update.

### Runtime and installer verification

The user explicitly authorized official Microsoft runtime downloads and app-local bundling. The downloaded VC80/VC90/VC120 archives and extracted CRT DLLs have valid Microsoft signatures; PE headers confirm x86. Cabinets and MSI data streams were read without running a runtime installer or its custom actions. Native activation-context checks passed for PS3 and Vita manifests without executing those encoders.

The local installer and ZIP contain 30 explicitly selected files, including the offline README images. Both were extracted into new project test folders and every file was compared against its SHA-256 inventory. The installer rejected a nonempty destination and preserved its sentinel file. The installer validates its complete embedded payload before copying, uses create-new output files, rejects unsafe paths and never modifies the registry or installs system runtimes.

All five wizard pages were rendered and inspected: welcome, license terms, destination, install confirmation and completion. Every page includes console artwork. The license text uses native CRLF line breaks and Next remains disabled until acceptance. No installation into AppData or another personal folder was performed during verification. No desktop or private-folder screenshot was captured.

### Public source and release scope

The public inventory is checked for generated binaries, downloaded material, caches, settings, recognized secret patterns and personal filesystem paths. Only original application assets and authorized native-window renders are included as images. Third-party icon notices remain intact. The source is MIT licensed to jotadev27; native codec and Microsoft runtime licenses remain separate.

Installer cleanup is limited to this project's installer directory and requires the explicit CleanInstaller switch. New files are built and verified before cleanup. The final output is the installer EXE, portable ZIP, SHA256SUMS.txt and English RELEASE-v1.0.txt.

No push, GitHub release creation, credential changes or system-wide dependency installation was performed. Local source commits use only the requested owner's identity, without co-author trailers.

## Limits

Static inspection and fingerprints do not establish 100% safety, signing reputation or zero antivirus alerts. PSP/PS3 execution remains blocked in the command sandbox. Physical console playback, Windows N feature availability, clean-machine behavior and third-party binary redistribution rights remain unverified. The local packages are preparation artifacts; confirm third-party terms before public binary upload.
