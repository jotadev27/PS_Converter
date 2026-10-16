# Third-party notices

PS_Converter's C# application code is MIT licensed. That license does not grant rights to native codecs, Microsoft runtimes or upstream icon assets.

## Lucide icons

Interface icons are unmodified official Lucide SVGs at upstream revision 7b0196e249ca09ca27c584c497aca9a8cab8a7f5. Their ISC and applicable Feather/MIT notices are preserved in assets/icons/LICENSE.txt and in licenses/Lucide.txt inside local release packages. The application logo is an original conversion-arrow mark.

## Native audio tools

The local build uses PSP_at3tool.exe, PS3_at3tool.exe, PSVita_at9tool.exe, PS4_at9tool.exe and lame.exe as separate processes. Original executable bytes and embedded notices are unchanged. The public source repository does not include those binaries.

The ATRAC tools' source, publisher authenticity and redistribution terms were not established. They remain outside the project's MIT license. Confirm the applicable rights before uploading binary packages to a public release. Static inspection and matching hashes are not proof that native code is safe.

The upstream [LAME project](https://lame.sourceforge.io/download.php) publishes its source and LGPL licensing information. The exact supplied binary's corresponding build/source provenance has not been independently established. Retain its notices and satisfy its applicable source/redistribution obligations before external distribution.

## App-local Windows runtimes

All ATRAC executables are x86, including when used on 64-bit Windows. Matching runtime files are included only in the locally generated installer/portable artifacts:

| Encoder | App-local runtime |
|---|---|
| PSP | The inspected x86 msvcr71.dll |
| PS3 | Microsoft VC80 CRT 8.0.50727.6195, x86, with its assembly manifest |
| PS Vita | Microsoft VC90 CRT 9.0.30729.6161, x86, with its assembly manifest |
| PS4 | Microsoft msvcr120.dll 12.0.40664.0, x86 |

The latter three runtimes were extracted as data from official Microsoft-signed download archives, with explicit authorization. The download archives and extracted CRT DLLs have valid Microsoft Authenticode signatures. PE headers confirm x86 architecture. The incompatible x64 PS4 runtime from the reference material is excluded.

App-local configuration maps the two older embedded runtime requests to their packaged servicing versions. It does not modify the encoder EXEs, the registry, system assemblies or system-wide publisher policy. Native activation-context validation reads the manifests without executing the encoders.

Official package sources:

- [VC++ 2005 SP1 MFC security update](https://www.microsoft.com/en-us/download/details.aspx?id=26347)
- [VC++ 2008 SP1 MFC security update](https://www.microsoft.com/en-us/download/details.aspx?id=26368)
- [Microsoft's runtime download reference](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist), including the VC++ 2013 x86 servicing download

Microsoft runtime files remain subject to Microsoft's license and redistribution terms. Available English notices extracted from the packages are retained in the local licenses folder. No system-wide runtime installer was executed. These legacy runtime versions are out of support; their inclusion is a compatibility measure, not a claim of current support or a security guarantee.

## File identity and verification limits

The embedded tools/codec-package.json catalog pins every codec, private runtime DLL, manifest and application configuration file by SHA-256. The builder rejects different bytes; the app verifies and holds those files open before execution. Extra adjacent DLLs or manifests are rejected.

Codec processes execute with the current user's permissions. Working directories, argument validation, output validation and Job Objects reduce concrete risks but are not an operating-system security sandbox. Signing the new application, antivirus reputation, clean-machine compatibility and physical-console playback have not been established.

Public source publication and public binary redistribution are separate decisions. The generated local release files are not a declaration that third-party redistribution has been cleared.
