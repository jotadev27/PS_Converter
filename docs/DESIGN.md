# Native interface design

The v1.0 visual update preserves the existing warm white, charcoal and muted slate palette. The installed IBM/Carbon design analysis informed square, thin-bordered surfaces, clear field outlines and a measured spacing rhythm; its brand colors and typography were not copied. Segoe UI remains the native Windows typeface.

## Decisions

- Audio and Console remain the two visible steps.
- The Convert button is centered and retains its existing conversion/cancellation handler.
- Playback controls are removed from the visible action row; conversion code is unchanged.
- The file panel uses square corners. Its toolbar and file list occupy separate layout rows, including with long filenames.
- General options keep editable bitrate and loop controls, with dark outlines and accessible names.
- The application and Options windows share the new, substantial conversion-arrow icon. Its transparent ICO contains 16, 24, 32, 48, 64, 128 and 256 pixel images.
- Interface symbols come from the exact bundled Lucide SVGs. Their paths are rasterized at build time with the existing Windows framework geometry parser; no SVG runtime dependency is installed.

Native keyboard focus, tab navigation, accessible control names, empty/ready/busy states and resizing are checked using WinForms tooling. The applicable accessibility, focus, contrast and content-overflow principles from Vercel's Web Interface Guidelines are adapted to native controls. Playwright does not operate this native-only interface and is not represented as a test that ran.

Application windows are rendered off-screen using DrawToBitmap; the desktop and personal folders are not captured. Published interface images contain only empty states or generated test audio filenames.

## Console artwork

The four transparent console illustrations are original generated assets, embedded in the executable. Their aspect ratio is preserved. They are not official product photography or a claim of endorsement. No external image service is contacted at runtime.

## Installer

The installer uses Inno Setup's classic native wizard, with standard Next, Back and Cancel controls. The approved conversion logo, muted sidebar and console illustrations appear on welcome and completion; intermediate pages show the same logo in the header. The English wizard includes license consent, a destination selector and install confirmation. Existing nonempty destinations are preserved. Application code and the portable archive are unchanged by this installer replacement.
