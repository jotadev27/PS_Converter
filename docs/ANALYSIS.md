# Reference analysis and implementation decisions

## What was established

The supplied reference source is a .NET Framework 4.8 C# WinForms wrapper. It does not implement the ATRAC algorithms. It calls native command-line encoders for PSP, PS3, PS Vita and PS4, an MP3 encoder, and playback/audio libraries. The new implementation is independently written C# and does not copy its code, application artwork, personal identifiers, credits or history.

Source inspection identified eight routes: WAV/MP3 to AT3, AT3 to WAV/MP3, WAV/MP3 to AT9, and AT9 to WAV/MP3. The command contract is `-e -br N` for encoding, `-d -repeat N` for decoding and `-V2` for MP3 encoding. Loop, superframe, band and PS4 PCM options are now represented by typed settings. Unsupported combinations fail before the encoder is invoked. Free-form argument injection is not supported.

The original layout groups conversion controls, a console row, file selection and status. Following the requested redesign, PS_converter uses a compact light interface with two numbered steps: Audio and Console. It includes console images, understated icons, an automatic format choice and one primary Convert button. Bitrate and technical settings are kept in Options.

## Defects addressed

| Observed defect | Source location | New behavior |
|---|---|---|
| Intermediate WAV shares the source basename and can collide with an existing WAV | At3Controller.cs:29–46; At9Controller.cs:29–46 | Per-conversion GUID workspace and anonymous input snapshot |
| Legacy move can delete an existing destination | ConversionService.cs:192–216 | Atomic move with numbered collision suffix; no deletion |
| PSP MP3 normalization preserves mono while WAV normalization requests stereo | ConversionService.cs:524–536,582–587 | Both paths produce 44.1 kHz stereo PCM16 for PSP |
| Same-rate float/24-bit WAV can bypass normalization | ConversionService.cs:351–357,524–536 | All encoding inputs are normalized to PCM16 |
| Only the first dropped file is accepted | MainForm.cs:92–96 | All supported dropped files enter the queue |
| Repeat selection stops at 99 | ToolOptionsDialog.cs:184 | Any positive representable count; codec compatibility still applies |
| Encoder wait cannot be cancelled and diagnostics accumulate in memory | ConversionService.cs:24–53 | Cancellation, optional deadline, bounded stream draining and Job Object lifecycle |
| Exit code zero is accepted without inspecting output | ConversionService.cs:46–55 | RIFF format/chunk checks or MP3 frame-header check before publication |
| Playback decodes the full track into managed memory | At9Player.cs:31–51 | Decode to a file and use streaming Windows waveaudio playback |
| Logs retain full source paths and command text | ConversionService.cs:72–169 | No persistent conversion logs |
| Codec identity and adjacent runtime files are not checked | ConversionService.cs:24–34 | Embedded SHA-256 catalog for packaged files; explicit approval for optional custom tools; files checked before each run |
| A required runtime file is present but omitted from project copy items | Reference project file:162–184 | Known codec files and the x86 PSP runtime are packaged explicitly; the reference PS4 runtime is x64 and excluded |

No explicit TODO or NotImplemented marker established an intentionally unfinished feature. The table records observable defects and missing behavior, rather than guessing the original developer's intent.

## REA static evidence

REA inspected the four encoder PE resource inventories without running them. All four are PE32 x86. Windows Authenticode inspection reported all four as `NotSigned`.

| Encoder | SHA-256 | REA resource evidence |
|---|---|---|
| PS3_at3tool.exe | 8ac653df80443000c8532052a7005167031b546143295aa8c2ab89f3697af3bb | ev_7ccb6ec888ad5e0aee70822faca5b3af43c3db9d8570d68669cd13e275269a09 |
| PSP_at3tool.exe | 823199113c59ffcd3fc3e1855dbb88a9cd44d29e82e7a7cfc42e8c92f2a61341 | ev_9c53a4d53ebc01964438afb5c47ccecf45afbd536416f12aa1c020346fe769de |
| PSVita_at9tool.exe | d57d5f24b3f733864d1ac24d5a9293269b7b0a113e28944dcd848a168bb69b63 | ev_7221ab8290d8fe39e90616858a76e8573cf6cd5adfae7210cbe2e26466760289 |
| PS4_at9tool.exe | 8228fbd4ea5552a1ea80ca8304c09104423d142313d6d80ea49d63c048c8d00b | ev_d83af47e0854518eb66b8af99905d249b92491cc4e6691ad5ce5917ca348bf8f |

Ghidra 12.1.4 observations through REA:

- PSP: 1002 recovered procedures and 115 defined strings in `ev_88a3741bf0cafdc394712a9c99920f94975e1e42fb240ed0976d4208c689cf10`. Help strings establish PCM16 input, loop/repeat and bitrate arguments in `ev_086c0c6219b4ba06379d4803ec18b5fa4dcba15f26272aa08376d14f948ee485`. A bounded name search for network, registry, process-launch and dynamic-loading routines matched only the `fopen` import in `ev_b543650e4bea9eb082f4b931204d16ff52bb2d953ac306a4762962677bf523d9`. That search is not a complete call-flow analysis.
- PS3: the recovered external inventory contains 52 entries from MSVCR80 and KERNEL32 in `ev_3bb18a15d004a85991eb255cbe418482e88f5bd635fd149798552a10f9d41ab9`.
- PS Vita: 69 external entries from MSVCR90 and KERNEL32, including `remove`, in `ev_9adeb8dd6243252e85c0afedc86374fa9a7393591516bf7bd0c7a8d4d3bda5cb`.
- PS4: 75 recovered external entries from MSVCR120 and KERNEL32 in `ev_e9fd6e33a1dbc7a2784bc743a547672f5fdf393056ab467c0cf8b239c51bb1a2`. These include private-profile INI read/write routines and `remove`. Those imports establish capabilities, not malicious behavior or observed execution. PCM, loop and repeat help strings are recorded in `ev_65646c2acd1870449905dbb93653799340982dbcec44800b44fed6b278fa82e3`.

The PS3/Vita/PS4 external inventories did not expose networking, registry modification, process creation or dynamic-library loading imports among their recovered entries. Runtime resolution, statically linked behavior, undefined strings, individual function control flow and native runtime DLL internals remain outside this bounded inspection. Complete inline records collected in this session are retained in the ignored `.analysis/rea-records.json`; that file is a record collection, not an REA export-format bundle.

## Security conclusion and residual limits

The inspected C# source showed no observed network exfiltration, persistence, credential access, security disabling or malicious routines. Concrete unsafe file handling and missing checks were replaced in the new implementation. This is a bounded source finding, not a malware certificate.

The supplied ATRAC executables have no source or redistribution terms established in the supplied tree. Their publisher authenticity, complete behavior and malware status cannot be established from metadata and import searches. Version 1.0.2 includes their unchanged, hash-pinned bytes in the local portable package to remove the confusing executable chooser from Convert. This is not a publicly licensed redistribution release. The older player, audio libraries and MP3 DLL remain excluded. The v1.0 package additionally includes app-local x86 VC80/VC90/VC120 runtimes extracted from verified official Microsoft packages. The reference PS4 runtime is x64 and remains excluded. See THIRD_PARTY.md for provenance, runtime and licensing limits.

Pinned files are held open against ordinary replacement during conversion. Private input copies and an isolated working directory protect the originals from normal command behavior. External codecs still execute with the user's permissions; Job Objects are lifecycle control, not a security sandbox. There is a launch-to-job-attachment interval, and concurrent same-user filesystem manipulation is not comprehensively defended. No guarantee of 100% safety, unlimited hardware resources or zero antivirus alerts is made.

Native MP3 decoding and resampling follow the Microsoft [Source Reader documentation](https://learn.microsoft.com/en-us/windows/win32/medfound/processing-media-data-with-the-source-reader) and its [audio resampling contract](https://learn.microsoft.com/en-us/windows/win32/api/mfreadwrite/nf-mfreadwrite-imfsourcereader-setcurrentmediatype). The new application requires Windows 10/11 rather than attempting older Windows support.

## Checklist status

| Requested outcome | Status |
|---|---|
| REA investigation of shipped encoders | Static PE inspection plus bounded native strings/import observations recorded |
| New C# application and modernized layout | Implemented and compiled |
| Eight source conversion routes and codec settings | Implemented; contract tested with synthetic fixtures, plus real Vita round trips |
| Source defects and missing batch/cancellation behavior | Addressed as listed above |
| Independent new code, artwork and packaging | New C# code and artwork implemented; unchanged codec binaries included separately; no new installer generated |
| Real ATRAC encoding/decoding and physical console playback | Vita and PS4 native round trips verified on generated audio with app-local runtimes; PSP/PS3 retain a sandbox loader restriction; physical console playback unverified |
| Fully independent ATRAC codec algorithms | Not implemented; the original also delegates these algorithms to native tools |
| Absolute safety or absence of all platform/codec limits | Cannot be established |
