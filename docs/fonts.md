# Font Strategy

HyperCard# aims to render classic Macintosh stacks as faithfully as possible while respecting font copyright. Original Mac system fonts (Geneva, Chicago, Monaco, etc.) are copyrighted by Apple Inc. and cannot be redistributed. Instead, HyperCard# uses a multi-tier fallback strategy that lets users supply original fonts themselves for maximum accuracy, with open-source substitutes as fallbacks.

## Font Resolution Priority

When HyperCard# needs a font (e.g. "Geneva 12pt"), it searches in this order:

### 1. User-Supplied Font Directory

Place original `.ttf` or `.otf` font files in a **`fonts/`** folder next to the HyperCard# application. The app scans this directory at startup and matches fonts by file name or embedded family name.

For example, if a stack uses Geneva, drop `Geneva.ttf` into the `fonts/` folder and HyperCard# will use it automatically.

### 2. System-Installed Fonts

HyperCard# checks whether the original Mac font is installed on your operating system:

- **macOS** — Many classic Mac fonts (Geneva, Monaco, Palatino, Times, Helvetica, Courier) ship with macOS and will be found automatically. No action needed.
- **Windows / Linux** — If you've installed classic Mac TrueType fonts on your system (e.g. from a legitimately owned copy of Mac OS), HyperCard# will detect and use them.

### 3. Embedded Open-Source Substitutes

The application bundles free, open-source fonts as reasonable stand-ins:

| Original Mac Font | Substitute | License |
|-------------------|-----------|---------|
| Chicago / Charcoal | [ChicagoFLF](https://github.com/pfcode/system7css) | MIT |
| Geneva / Helvetica | [Noto Sans](https://fonts.google.com/noto/specimen/Noto+Sans) | SIL Open Font License |

These provide a readable, visually similar experience when original fonts are unavailable.

### 4. Common Cross-Platform Fonts

For fonts not covered above, HyperCard# maps to widely available system fonts:

| Original Mac Font | Substitute |
|-------------------|-----------|
| New York | Times New Roman |
| Monaco | Courier New |
| Courier | Courier New |
| Times | Times New Roman |
| Palatino | Palatino Linotype |
| Bookman | Georgia |

### 5. System Default

If none of the above resolves, SkiaSharp's default typeface is used as an absolute last resort.

## Where to Find Original Mac Fonts

If you want pixel-perfect accuracy and own a legitimate copy of classic Mac OS, you can extract the original TrueType fonts:

- **From a Mac running macOS** — Classic Mac fonts like Geneva, Monaco, and Chicago are in `/System/Library/Fonts/` or `/Library/Fonts/`. Copy the `.ttf` files to the HyperCard# `fonts/` folder.

- **From a System 7 / Mac OS 8–9 installation** — The fonts live in the `System Folder:Fonts` folder. If you have a disk image (`.img`) or an emulated Mac, copy the font suitcase files out. TrueType fonts from Mac OS 8+ are generally in standard `.ttf` format.

- **From Apple's legacy font downloads** — Apple previously offered TrueType versions of their classic fonts. If you archived these, they work directly.

> **Note:** HyperCard# does not redistribute any Apple-copyrighted fonts. Users who want original fonts must supply them from their own legitimate copies of Mac OS.

## Font ID Reference

HyperCard stacks reference fonts by numeric ID. Here are the standard Mac font IDs:

| ID | Font Name |
|----|-----------|
| 0 | Chicago (System) |
| 1 | Geneva (Application) |
| 2 | New York |
| 3 | Geneva |
| 4 | Monaco |
| 13 | Zapf Dingbats |
| 14 | Bookman |
| 16 | Palatino |
| 20 | Times |
| 21 | Helvetica |
| 22 | Courier |
| 23 | Symbol |

Stacks may also reference fonts by name in their Font Table (FTBL block), including third-party fonts like "GeoSlab703 Lt BT Light" or "Verdana". These are resolved by the same priority chain — place the matching `.ttf` file in the `fonts/` directory for best results.

## Philosophy

HyperCard# is a **digital preservation tool**. Our goal is to present classic HyperCard stacks as accurately as possible on modern systems. We believe:

- **Fidelity matters.** The visual experience of a HyperCard stack — its fonts, layout, and pixel-level details — is part of what we're preserving.
- **Copyright matters.** We don't redistribute copyrighted fonts. Instead, we make it easy for users who own legitimate copies to use them.
- **Graceful degradation.** When original fonts aren't available, we fall back to the best open-source substitutes we can find. The stack should always be readable, even if not pixel-perfect.
- **Simplicity.** Drop font files in a folder — no configuration, no setup wizards.

This approach is analogous to how classic computing emulators handle system ROMs: the emulator doesn't include copyrighted ROM files, but makes it straightforward for users to supply their own.
