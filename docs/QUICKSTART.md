# Start converting

## Choose your download

**Installer:** run `PS_Converter-v1.0-setup.exe`, read and accept the license terms, select an empty installation folder, then click Install. The wizard includes its files and artwork; it does not clone a repository, contact an update service, change the registry or install system runtimes. You can launch the application from its final page.

**Portable:** extract all of `PS_Converter-v1.0-portable.zip` into a writable local folder, then double-click `PS_converter.exe`. Keep the entire `codecs` folder beside the EXE. You do not need Git or the source repository to use the release download.

Windows 10/11, .NET Framework 4.8 and Windows Media Foundation are required. Windows N editions require the Media Feature Pack. Matching x86 codec runtimes are included app-locally; physical-console playback and a pristine Windows installation are not verified.

## Convert in three steps

1. Drop an MP3 or WAV into Audio, or click Add files. Multiple files are supported.
2. Select PSP, PS3, PS Vita or PS4 using its image.
3. Press Convert. The button changes to Cancel while the queue is running.

PSP/PS3 output AT3; Vita/PS4 output AT9. Output defaults to the first audio file's folder. Save to chooses another folder. Existing files are preserved; name collisions receive a numbered suffix.

To decode, add an AT3/AT9 file, select the matching console, then choose WAV or MP3 in Format. An AT3 file's extension alone cannot distinguish PSP from PS3, so select the correct profile manually when needed.

## Options

- General: bitrate and loop mode. Defaults are selected automatically.
- Advanced: sample-based loops, repeat count, sample rate and codec-specific settings.
- Encoders: an optional custom override. Normal release use does not require selecting an executable.

The two playback buttons were removed from the main window. To check an encoded file, convert it back to WAV and listen using your preferred audio player. Hardware compatibility also depends on the consuming game, application or theme.

## Files and recovery

Settings are stored in `data/settings.json` beside the application. Anonymous input copies and intermediate audio remain in `.psconverter-work` under the output folder. They are preserved after cancellation or failure; remove them manually when no longer needed and reserve enough disk space.

If the application reports missing codec files, extract the complete ZIP again into a new folder. Hash mismatches indicate changed files; do not bypass the check by renaming a different executable. Codec errors can also reflect unsupported bitrates, container limits or Windows loader restrictions.

The application imposes no artificial file-count, track-duration or usage caps. Hardware, storage, integer ranges, codec capabilities and legacy RIFF container limits still apply. For licensing and native-code verification limits, read THIRD_PARTY.md.
