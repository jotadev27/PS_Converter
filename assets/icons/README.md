# Interface icons

The SVG files in this folder are unmodified Lucide assets from upstream revision `7b0196e249ca09ca27c584c497aca9a8cab8a7f5` of [lucide-icons/lucide](https://github.com/lucide-icons/lucide). Their notices are preserved in `LICENSE.txt`.

`build.ps1` rasterizes these exact SVG paths at 128 × 128 using the existing Windows framework geometry parser. The native UI scales the embedded images with their original aspect ratio and applies the control's foreground color. No browser, JavaScript package or runtime SVG dependency is needed.

The application logo is a separate original conversion-arrow mark, drawn by `Theme.DrawMark` and exported by `BuildAssets` with transparent corners at the standard Windows icon sizes.
