# HyperCard#

A cross-platform, open source HyperCard stack player and HyperTalk interpreter built with C# and .NET 8.

## What It Does

HyperCard# opens classic Mac HyperCard stacks natively on Windows, macOS, and Linux — no Mac emulation required. It handles stacks delivered as raw files, StuffIt archives (.sit), BinHex (.hqx), or Mac disk images (.img), with support for black-and-white and color rendering, interactive card navigation, HyperTalk script execution, and Mac `snd ` audio playback.

## Current Features

- **Stack binary parser** — reads STAK, MAST, LIST, PAGE, CARD, BKGD, BMAP, STBL, FTBL blocks with full big-endian binary parsing
- **WOBA bitmap decoder** — decompresses HyperCard's proprietary WOBA-encoded bitmaps (RLE + XOR delta + bit-packing)
- **Card rendering** — SkiaSharp-based 1-bit bitmap rendering with background/card compositing and part overlays
- **Color rendering** — AddColor (HCcd/HCbg) resource support composites color fills under BMAP bitmaps; PICT v2 resource decoding
- **Styled text rendering** — per-run font, size, and style (bold/italic/underline) via `StyleTableBlock` + `FontTableBlock`; Geneva, Chicago, Monaco, Palatino, Times substitution
- **MacRoman text decoding** — full 128-entry Mac OS Roman → Unicode lookup table (0x80–0xFF); replaces Latin-1 approximation throughout
- **HyperTalk interpreter** — lexer, recursive-descent parser, tree-walking interpreter with message passing (button → card → background → stack)
  - `go`, `put`, `set`, `show`/`hide`, `type`, `click at`, `wait`, `send`, `find`, `answer`, `ask`
  - Chunk expressions: `char`, `word`, `item`, `line`
  - String, math, date/time built-in functions
  - Visual effects: dissolve, wipe, iris, scroll, checkerboard
  - `play` / `stop sound` via Mac `snd ` resources
  - `go home` triggers file-open picker
- **Sound playback** — Mac `snd ` Format 1/2 resources decoded to WAV; LibVLC playback
- **Container format support** — all native C#, no external dependencies:
  - MacBinary and AppleSingle wrappers
  - BinHex 4.0 (.hqx) decoding
  - StuffIt (.sit) archive extraction with LZW decompression
  - DiskCopy 4.2 disk image parsing
  - HFS and HFS+ filesystem reading with B-tree catalog traversal
  - Raw disk image scanning for embedded STAK blocks
- **Format robustness** — HyperCard 1.x stacks detected and flagged; password-protected stacks detected and surfaced as visible status warnings; graceful degradation on unknown blocks
- **Multi-stack disk images** — disk images containing multiple stacks present a System 7-styled picker dialog
- **Auto-detection** — drop any supported file and the container pipeline automatically unwraps layers to find the stack
- **Auto-fit scaling** — card content scales with window size; Ctrl+1/2/3/4 for zoom presets
- **XCMD stub layer** — unsupported external commands log gracefully instead of crashing

## Supported Input Formats

| Format | Extension | Status |
|--------|-----------|--------|
| Raw HyperCard stack | (no extension) | ✅ Working |
| MacBinary | .bin | ✅ Working |
| AppleSingle | .as | ✅ Working |
| BinHex 4.0 | .hqx | ✅ Working |
| StuffIt archive | .sit | ✅ Working |
| DiskCopy 4.2 image | .img | ✅ Working |
| HFS disk image | .img | ✅ Working |
| HFS+ disk image | .img | ✅ Working |

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| Ctrl+O | Open file |
| Ctrl+M | Switch stack (multi-stack images) |
| Ctrl+1/2/3/4 | Zoom 1x / 2x / 3x / 4x |
| Left/Right | Previous / next card |
| Home/End | First / last card |
| Ctrl+H | Help |

## Architecture

```
HyperCardSharp/
├── src/
│   ├── HyperCardSharp.Core/          # Binary parsing, stack model, containers
│   │   ├── Binary/                   # BigEndianReader, MagicDetector, BlockHeader
│   │   ├── Stack/                    # STAK/MAST/LIST/PAGE/CARD/BKGD/BMAP parsers
│   │   ├── Parts/                    # Button, field, part content models
│   │   ├── Bitmap/                   # WOBA decoder, BitmapImage
│   │   ├── Text/                     # MacRomanEncoding (full 0x80–0xFF table)
│   │   └── Containers/               # MacBinary, AppleSingle, BinHex, StuffIt,
│   │                                 # DiskCopy, HFS, HFS+, raw scanner
│   ├── HyperCardSharp.HyperTalk/    # Lexer, parser (AST), interpreter, XCMD stubs
│   ├── HyperCardSharp.Rendering/    # SkiaSharp card/bitmap/color/text rendering
│   └── HyperCardSharp.App/          # AvaloniaUI desktop application
└── tests/                            # Unit tests (Core, HyperTalk, Rendering)
```

## Building

```bash
# Prerequisites: .NET 8 SDK
dotnet build
dotnet run --project src/HyperCardSharp.App
```

## Self-Contained Publish

```bash
# Windows
dotnet publish src/HyperCardSharp.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# macOS (Apple Silicon)
dotnet publish src/HyperCardSharp.App -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true

# Linux
dotnet publish src/HyperCardSharp.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## Known Gaps / Future Work

- PICT resource rendering (complex opcode set; partially implemented)
- HyperCard 1.x full format support (detected and flagged; rendering may be partial)
- Password-protected stack decryption (out of scope — stacks display with a warning)
- QuickTime MOV playback (LibVLC stub in place; video layout not yet wired)
- Foreign language script system encodings beyond Mac Roman

See [docs/stack-format.md](docs/stack-format.md) for binary format research notes and [docs/hypertalk-coverage.md](docs/hypertalk-coverage.md) for HyperTalk command coverage.

## References

- [HyperCardPreview by Pierre Lorenzi](https://github.com/PierreLorenzi/HyperCardPreview) — deepest binary format work, WOBA decoder (Swift)
- [hypercard4net](https://github.com/giawa/hypercard4net) — partial C# HyperCard parser
- [ViperCard](https://github.com/vipercard/vipercard) — browser-based HyperCard reimplementation
- [OpenXION](http://www.openxion.org/) — open source HyperTalk interpreter (Java)
- [Definitive Guide to HC Stack File Format](https://www.kreativekorp.com/swdownload/wildfire/HC%20FILE%20FORMAT%202010.TXT)
- [AddColor Resource Format](https://hypercard.org/addcolor_resource_format/)

## Contributing

Contributions welcome — especially:

- Stack format reverse engineering and testing against real stacks
- HyperTalk language edge cases and coverage gaps
- PICT rendering improvements
- QuickTime MOV playback integration
- HyperCard 1.x format support

Please file an issue before opening a large pull request so we can discuss approach.

## License

MIT


## What It Does

HyperCard# opens classic Mac HyperCard stacks natively on Windows, macOS, and Linux — no Mac emulation required. It handles stacks delivered as raw files, StuffIt archives (.sit), BinHex (.hqx), or Mac disk images (.img), with support for black-and-white bitmap rendering, interactive card navigation, and HyperTalk script execution.

## Current Features

- **Stack binary parser** — reads STAK, MAST, LIST, PAGE, CARD, BKGD, BMAP, STBL, FTBL blocks with full big-endian binary parsing
- **WOBA bitmap decoder** — decompresses HyperCard's proprietary WOBA-encoded bitmaps (RLE + XOR delta + bit-packing)
- **Card rendering** — SkiaSharp-based 1-bit bitmap rendering with background/card compositing and part overlays
- **HyperTalk interpreter** — lexer, recursive-descent parser, tree-walking interpreter with message passing (button → card → background → stack)
- **Container format support** — all native C#, no external dependencies:
  - MacBinary and AppleSingle wrappers
  - BinHex 4.0 (.hqx) decoding
  - StuffIt (.sit) archive extraction with LZW decompression
  - DiskCopy 4.2 disk image parsing
  - HFS and HFS+ filesystem reading with B-tree catalog traversal
  - Raw disk image scanning for embedded STAK blocks
- **Multi-stack disk images** — disk images containing multiple stacks present a System 7-styled picker dialog
- **Auto-detection** — drop any supported file and the container pipeline automatically unwraps layers to find the stack
- **Auto-fit scaling** — card content scales with window size; Ctrl+1/2/3/4 for zoom presets
- **XCMD stub layer** — unsupported external commands log gracefully instead of crashing

## Supported Input Formats

| Format | Extension | Status |
|--------|-----------|--------|
| Raw HyperCard stack | (no extension) | Working |
| MacBinary | .bin | Working |
| AppleSingle | .as | Working |
| BinHex 4.0 | .hqx | Working |
| StuffIt archive | .sit | Working |
| DiskCopy 4.2 image | .img | Working |
| HFS disk image | .img | Working |
| HFS+ disk image | .img | Working |

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| Ctrl+O | Open file |
| Ctrl+M | Switch stack (multi-stack images) |
| Ctrl+1/2/3/4 | Zoom 1x / 2x / 3x / 4x |
| Left/Right | Previous / next card |
| Home/End | First / last card |

## Architecture

```
HyperCardSharp/
├── src/
│   ├── HyperCardSharp.Core/          # Binary parsing, stack model, containers
│   │   ├── Binary/                   # BigEndianReader, MagicDetector, BlockHeader
│   │   ├── Stack/                    # STAK/MAST/LIST/PAGE/CARD/BKGD/BMAP parsers
│   │   ├── Parts/                    # Button, field, part content models
│   │   ├── Bitmap/                   # WOBA decoder, BitmapImage
│   │   └── Containers/               # MacBinary, AppleSingle, BinHex, StuffIt,
│   │                                 # DiskCopy, HFS, HFS+, raw scanner
│   ├── HyperCardSharp.HyperTalk/    # Lexer, parser (AST), interpreter, XCMD stubs
│   ├── HyperCardSharp.Rendering/    # SkiaSharp card/bitmap rendering
│   └── HyperCardSharp.App/          # AvaloniaUI desktop application
└── tests/                            # Unit tests
```

## Building

```bash
# Prerequisites: .NET 8 SDK
dotnet build
dotnet run --project src/HyperCardSharp.App
```

## Known Format Research Gaps

The HyperCard binary format is partially reverse-engineered. Areas needing further work:

- PICT resource rendering (complex, inconsistent across HC versions)
- Styled text runs inside fields (font/size/style spans)
- Color rendering via AddColor XCMD data (HCcd/HCbg resources)
- HyperCard 2.4 password encryption
- HyperCard 1.x vs 2.x format divergences
- Foreign language script system encodings

### References

- [HyperCardPreview by Pierre Lorenzi](https://github.com/PierreLorenzi/HyperCardPreview) — deepest binary format work, WOBA decoder (Swift)
- [hypercard4net](https://github.com/giawa/hypercard4net) — partial C# HyperCard parser
- [ViperCard](https://github.com/vipercard/vipercard) — browser-based HyperCard reimplementation
- [OpenXION](http://www.openxion.org/) — open source HyperTalk interpreter (Java)
- [Definitive Guide to HC Stack File Format](https://www.kreativekorp.com/swdownload/wildfire/HC%20FILE%20FORMAT%202010.TXT)

## Contributing

Contributions welcome — especially:
- Stack format reverse engineering and testing against real stacks
- HyperTalk language edge cases and coverage
- PICT rendering research
- Color (AddColor) support
- QuickTime MOV playback integration

## License

MIT
