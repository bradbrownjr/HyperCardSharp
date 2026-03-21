# Agent Guide for HyperCardSharp

## Product Purpose

HyperCardSharp is a cross-platform, native HyperCard stack player and HyperTalk interpreter for retro computing enthusiasts and digital preservationists. It opens classic Mac HyperCard stacks directly on modern Windows, macOS, and Linux systems — no Mac emulation. It handles stacks delivered as raw files, StuffIt (.sit) archives, or Mac disk images (.img), with support for B&W and color display modes, embedded QuickTime MOV playback, and HyperTalk script execution.

## Technical Stack

- **Language:** C# 12 / .NET 8 (LTS)
- **UI Framework:** AvaloniaUI 11.x (cross-platform, MVVM with CommunityToolkit.Mvvm)
- **Rendering:** SkiaSharp via ICustomDrawOperation for pixel-level bitmap rendering
- **Media Playback:** LibVLCSharp + LibVLCSharp.Avalonia (QuickTime MOV, audio)
- **Binary Parsing:** Span<byte> + BinaryPrimitives (big-endian, zero-allocation)
- **Container Formats:** All native C# — no external tool dependencies
- **Target Platforms:** Windows 11, macOS, Linux
- **Distribution:** Self-contained .NET publish (no runtime install required)

## Architecture

```
HyperCardSharp/
├── src/
│   ├── HyperCardSharp.Core/          # Binary parsing, stack model, containers
│   │   ├── Binary/                   # BigEndianReader, MagicDetector, BlockHeader
│   │   ├── Stack/                    # STAK/MAST/LIST/PAGE/CARD/BKGD/BMAP block parsers
│   │   ├── Parts/                    # Button, field, part content models
│   │   ├── Bitmap/                   # WOBA decoder, BitmapImage
│   │   ├── Containers/               # StuffIt, DiskCopy, HFS, MacBinary, AppleSingle, ResourceFork
│   │   └── Resources/                # PICT, snd, icon, AddColor decoders
│   ├── HyperCardSharp.HyperTalk/    # Lexer, parser (AST), interpreter, XCMD stubs
│   ├── HyperCardSharp.Rendering/    # SkiaSharp card/part/bitmap/color rendering
│   └── HyperCardSharp.App/          # AvaloniaUI application (views, viewmodels, services)
├── tests/                            # Unit tests for Core, HyperTalk, Rendering
└── docs/                             # PLAN.md, stack-format.md, hypertalk-coverage.md
```

## Supported Input Formats

- **Raw HyperCard stacks** — STAK magic at offset 0x04, big-endian block structure
- **StuffIt archives (.sit)** — SIT! magic, native C# LZW decompression
- **Mac disk images (.img)** — DiskCopy 4.2 format, native C# HFS filesystem parser
- **MacBinary / AppleSingle / AppleDouble** — wrapper formats preserving resource forks
- **Auto-detection** — MagicDetector identifies format from first bytes, chains extraction

## Display Modes

- **Black & White** — authentic 1-bit rendering at 512×342 (classic Mac 128K/Plus/SE)
- **Color** — full color for HC 2.x stacks with AddColor XCMD data (HCcd/HCbg resources)
- Mode switching via UI toggle — same data, different rendering presentation

## Known Stack Format Research Areas

The HyperCard binary format is partially reverse-engineered. These areas need further work:

- `PICT` resource rendering (complex, inconsistent across HC versions)
- Styled text runs inside fields (font/size/style spans)
- HyperCard 2.4 password encryption
- Undecoded card layout flags
- HyperCard 1.x vs 2.x format divergences
- Foreign language script system encodings

Primary references:
- [HyperCardPreview by Pierre Lorenzi](https://github.com/PierreLorenzi/HyperCardPreview) — deepest binary format work, WOBA decoder (Swift)
- [hypercard4net](https://github.com/giawa/hypercard4net) — partial C# HyperCard parser
- [ViperCard](https://github.com/vipercard/vipercard) — browser-based HyperCard reimplementation
- [OpenXION](http://www.openxion.org/) — open source HyperTalk interpreter (Java)
- [Definitive Guide to HC Stack File Format](https://www.kreativekorp.com/swdownload/wildfire/HC%20FILE%20FORMAT%202010.TXT)
- [AddColor Resource Format](https://hypercard.org/addcolor_resource_format/)
- [thecloudexpanse/sit](https://github.com/thecloudexpanse/sit) — StuffIt LZW reference (C)
- [libfshfs](https://github.com/libyal/libfshfs) — HFS filesystem documentation
- [HFSExplorer](https://github.com/unsound/hfsexplorer) — HFS parser reference (Java)

## Engineering Principles

- Keep the codebase **DRY** — no duplicated logic across parsers, renderers, or interpreters.
- Follow **SOLID** principles — especially single responsibility in the parser and interpreter layers.
- Keep solutions **KISS** — simple, explicit, and maintainable over clever.
- Favor clear architecture and extensibility over short-term shortcuts.
- Prefer cohesive refactors over layered quick fixes.

## Root-Cause Policy

- Never patch symptoms when resolving issues.
- Always research and identify the root cause before implementing a fix.
- Resolve root causes thoroughly, even when the correct fix is invasive.
- Maintain a strong foundation-first mindset for long-term maintainability.

## Planning and Collaboration Rules

- Treat every user question as requiring a direct answer.
- Do not treat questions as rhetorical.
- Answer user questions before making code changes.
- Before implementing a feature or large change, present a clear plan of action.
- Before implementing a feature or large change, present open questions that affect implementation.
- Before implementing a feature or large change, present risks or concerns.
- Before implementing a feature or large change, present suggestions and tradeoffs.
- For large changes, get alignment on the plan before implementation.

## Always/Never Memory Protocol

- If the user says to "always" or "never" do something, treat it as an instruction to update `AGENTS.md` with that rule.
- `AGENTS.md` is the shared memory for this project across all AI assistants.
- If an instruction is not written in `AGENTS.md`, assume it may be forgotten in future sessions.
- When adding an always/never rule, capture it as a clear, testable directive.

## User Experience Requirements

- Stacks open with a single file picker action — no configuration required to view a basic stack.
- Accepts raw stacks, .sit archives, and .img disk images transparently via auto-detection.
- Unsupported features (XCMDs, missing codecs, unknown resource types) degrade gracefully with a visible log entry, never a crash.
- HyperTalk script errors surface as readable messages, not raw exceptions.
- The player presents cards at authentic HyperCard resolution (512×342 base), with optional scaling.
- B&W and Color display modes are toggled via the View menu.

## Implementation Expectations

- Preserve architectural consistency — parser, renderer, and interpreter remain decoupled.
- Keep the HyperTalk interpreter's AST explicit and testable.
- Ensure stack parsing is traceable — unknown blocks should be logged with offset and length, not silently skipped.
- Update documentation when behavior, architecture, or format research findings change.

## Definition of Done

- The change solves the validated root cause.
- The implementation aligns with DRY, SOLID, and KISS.
- Unsupported features degrade gracefully without crashing.
- Tests or validation steps cover the changed behavior where possible.
- Related documentation and format research notes are updated.
