# HyperCard# — Full Implementation Plan

## Context

We are building a native, cross-platform HyperCard stack viewer in C# / .NET 8 that opens classic Mac HyperCard files directly — no Mac emulation. The project must handle stacks arriving as raw files, StuffIt archives, or disk images. It must support B&W and color display modes, embedded QuickTime MOV playback via LibVLC, and a HyperTalk script interpreter. The goal is to create a usable foundation that attracts community contributors interested in retro computing preservation.

Three sample files drive initial development:
- `NEUROBLAST_HyperCard` — raw HC 2.x stack (STAK magic, version 10, ~70 cards)
- `NEUROBLAST_Cyberdelia.sit` — StuffIt archive containing a stack
- `neuroblast.img` — DiskCopy 4.2 disk image with HFS filesystem

Key existing references:
- **hypercard4net** (giawa/hypercard4net) — partial C# HyperCard parser
- **HyperCardPreview** (Pierre Lorenzi, Swift) — most thorough binary format documentation and WOBA decoder
- **ViperCard** — browser-based HyperCard reimplementation
- **OpenXION** — open source HyperTalk interpreter (Java)

---

## Solution Structure

```
HyperCardSharp/
├── HyperCardSharp.sln
├── src/
│   ├── HyperCardSharp.Core/              # Binary parsing, stack model, containers
│   │   ├── Binary/
│   │   │   ├── BigEndianReader.cs         # Span<byte> + BinaryPrimitives wrapper
│   │   │   ├── MagicDetector.cs           # Auto-detect format from magic bytes
│   │   │   └── BlockHeader.cs             # 16-byte block header record
│   │   ├── Stack/
│   │   │   ├── StackFile.cs               # Top-level parsed stack model
│   │   │   ├── StackParser.cs             # Block enumeration + dispatch
│   │   │   ├── StackBlock.cs              # STAK header (version, card count, patterns)
│   │   │   ├── MasterBlock.cs             # MAST offset index
│   │   │   ├── ListBlock.cs / PageBlock.cs
│   │   │   ├── CardBlock.cs / BackgroundBlock.cs
│   │   │   ├── BitmapBlock.cs             # BMAP block (WOBA compressed)
│   │   │   ├── StyleTableBlock.cs / FontTableBlock.cs
│   │   │   └── UnknownBlock.cs            # Catch-all, logs offset+size
│   │   ├── Parts/
│   │   │   ├── Part.cs / ButtonPart.cs / FieldPart.cs / PartContent.cs
│   │   ├── Bitmap/
│   │   │   ├── WobaDecoder.cs             # WOBA decompression (hardest algorithm)
│   │   │   └── BitmapImage.cs             # Decoded 1-bit bitmap
│   │   ├── Containers/
│   │   │   ├── IContainerExtractor.cs
│   │   │   ├── StuffItExtractor.cs        # SIT! native LZW decompression
│   │   │   ├── DiskCopyExtractor.cs       # DiskCopy 4.2 header parsing
│   │   │   ├── HfsReader.cs              # Native HFS filesystem + B-tree
│   │   │   ├── MacBinaryExtractor.cs / AppleSingleExtractor.cs
│   │   │   └── ResourceForkParser.cs
│   │   └── Resources/
│   │       ├── PictDecoder.cs / SoundDecoder.cs / IconDecoder.cs
│   │       └── AddColorDecoder.cs         # HCcd/HCbg color overlay data
│   │
│   ├── HyperCardSharp.HyperTalk/         # Lexer, parser, interpreter
│   │   ├── Lexer/   (Token.cs, TokenType.cs, Lexer.cs)
│   │   ├── Parser/  (Ast/*.cs, Parser.cs)
│   │   ├── Interpreter/ (Interpreter.cs, Environment.cs, MessagePassing.cs, BuiltInCommands.cs)
│   │   └── Xcmd/    (IXcmdHandler.cs, XcmdStubHandler.cs, XcmdRegistry.cs)
│   │
│   ├── HyperCardSharp.Rendering/         # SkiaSharp card rendering
│   │   ├── CardRenderer.cs / PartRenderer.cs / BitmapRenderer.cs
│   │   ├── ColorRenderer.cs / TextRenderer.cs / FontMapper.cs
│   │   └── RenderMode.cs                 # Enum: BlackAndWhite, Color
│   │
│   └── HyperCardSharp.App/               # AvaloniaUI desktop application
│       ├── Views/ (MainWindow, CardView, MessageLogView)
│       ├── ViewModels/ (MainWindowViewModel, CardViewModel, StackViewModel)
│       ├── Controls/ (SkiaCardControl.cs — ICustomDrawOperation)
│       └── Services/ (FileOpenService.cs, MediaService.cs)
│
├── tests/
│   ├── HyperCardSharp.Core.Tests/
│   ├── HyperCardSharp.HyperTalk.Tests/
│   └── HyperCardSharp.Rendering.Tests/
│
└── docs/
    ├── PLAN.md                            # This file
    ├── stack-format.md                    # Evolving binary format documentation
    └── hypertalk-coverage.md              # HyperTalk command coverage tracker
```

## NuGet Packages

| Package | Project | Purpose |
|---------|---------|---------|
| Avalonia (11.x) | App | UI framework |
| Avalonia.Desktop | App | Desktop support |
| Avalonia.Themes.Fluent | App | Theme |
| SkiaSharp (2.88+) | Rendering | Bitmap/canvas rendering |
| LibVLCSharp + LibVLCSharp.Avalonia | App | Video/audio playback |
| VideoLAN.LibVLC.Windows | App | LibVLC native binaries |
| CommunityToolkit.Mvvm | App | MVVM source generators |
| Microsoft.Extensions.Logging.Abstractions | Core | Structured logging |
| xunit + FluentAssertions | Tests | Unit testing |

---

## Implementation Phases

### Phase 0: Scaffolding
Install .NET 8 SDK. Create solution, all projects, wire references. Minimal Avalonia window at 512x342. `dotnet build` + `dotnet run` succeed. **Commit.**

### Phase 1: Binary Infrastructure + Stack Header
- `BigEndianReader` — Span<byte> wrapper with BinaryPrimitives for big-endian reads
- `BlockHeader` — 16-byte record (size, type, ID, filler)
- `MagicDetector` — identify STAK, SIT!, DiskCopy from first bytes
- `StackParser.EnumerateBlocks()` — walk file, yield headers, log unknowns
- `StackBlock` — parse STAK (version at +0x10, card count, dimensions, patterns)
- `MasterBlock` — parse MAST offset table
- Unit tests with sample file

**Milestone:** Print block inventory from NEUROBLAST_HyperCard. **Commit.**

### Phase 2: Card, Background, and Part Parsing
- `CardBlock` / `BackgroundBlock` — header + part iteration
- `ButtonPart` / `FieldPart` — rect, style flags, name, script text
- `PartContent` — text + style references
- `FontTableBlock` / `StyleTableBlock` — font ID mapping, style runs
- `ListBlock` / `PageBlock` — card ordering

**Milestone:** Extract all card names, button labels, field text, scripts. **Commit.**

### Phase 3: WOBA Bitmap Decompression
- `BitmapBlock` — parse BMAP header (dirty rect, mask/image sizes)
- `WobaDecoder` — implement WOBA (RLE + XOR delta + bit-packing). Reference HyperCardPreview's Swift decoder
- `BitmapImage` — decoded 1-bit pixel data

**Milestone:** Decode all BMAPs, export as PNG, visually verify. **Commit.**

### Phase 4: Basic AvaloniaUI Rendering
- `BitmapRenderer` — 1-bit BitmapImage to SKBitmap
- `CardRenderer` — composite background + card bitmaps + part overlays
- `PartRenderer` — button outlines, field borders
- `TextRenderer` + `FontMapper` — styled text with Mac font substitution
- `SkiaCardControl` — ICustomDrawOperation for Avalonia
- Keyboard nav (arrow keys = prev/next card)

**Milestone:** View all NEUROBLAST cards with bitmaps and button/field rendering. **Commit.**

### Phase 5: HyperTalk Lexer + Parser
- Lexer: keywords, identifiers, strings, numbers, operators, `--` comments
- Parser: recursive descent to AST (HandlerNode, CommandNode, IfNode, RepeatNode, ExpressionNode, chunk expressions)
- Parse all scripts from sample stack

**Milestone:** Parse 100% of NEUROBLAST scripts into AST with zero errors. **Commit.**

### Phase 6: HyperTalk Interpreter + Message Passing
- Tree-walking interpreter
- Variable scoping (local, global, `it`)
- Message hierarchy: button -> card -> background -> stack
- Built-in commands: `go to card`, `visual effect`, `put`, `set`, `answer`, `ask`
- `XcmdStubHandler` — log unsupported commands, never crash
- Wire UI: click -> hit-test parts -> dispatch mouseUp

**Milestone:** Click buttons in NEUROBLAST -> cards navigate via HyperTalk. **Commit.**

### Phase 7: Container Format Support (All Native C#)
- `MacBinaryExtractor` / `AppleSingleExtractor` — header parsing, extract data + resource forks
- `ResourceForkParser` — 256-byte header, type list, reference list, data section
- `StuffItExtractor` — native C# LZW decompression (reference: thecloudexpanse/sit C implementation)
- `DiskCopyExtractor` — parse DiskCopy 4.2 header (84 bytes), extract raw disk data
- `HfsReader` — native C# HFS filesystem parser: Master Directory Block, catalog B-tree traversal, file extraction by type code. Reference: libfshfs documentation + HFSExplorer Java source
- `MagicDetector` chains: detect container -> extract -> detect inner -> parse
- No external tool dependencies — fully self-contained

**Milestone:** Open all 3 sample files (raw, .sit, .img) and render cards from each. **Commit.**

### Phase 8: Color Rendering + B&W Mode Toggle
- `AddColorDecoder` — parse HCcd/HCbg resources from resource fork
- `ColorRenderer` — composite color overlay onto B&W bitmap
- `RenderMode` toggle in UI menu (View -> Black & White / Color)
- B&W mode: threshold quantize all rendering to 1-bit
- `PictDecoder` — partial QuickDraw replay (common opcodes)

**Milestone:** Toggle B&W / Color mode. PICT resources render. **Commit.**

### Phase 9: Media Playback
- `SoundDecoder` — parse Mac snd resource, extract PCM samples
- `MediaService` — LibVLCSharp wrapper for MOV + audio playback
- Wire `play` HyperTalk command to audio system
- Embed VideoView in Avalonia for MOV files

**Milestone:** `play` command triggers audio. MOV files play in embedded viewer. **Commit.**

### Phase 10: Polish + Extended HyperTalk
- Extended HyperTalk: `repeat with/while/until`, `do`, `send`, string/math/date functions
- Chunk expressions: `char`, `word`, `item`, `line` of containers
- Visual effects: dissolve, wipe, iris, checkerboard (SkiaSharp transitions)
- Scrolling fields, Find command
- Menu bar, drag-and-drop, recent files
- Error display in status bar / log panel

**Milestone:** Polished, usable viewer for community release. **Commit + tag v0.1.0.**

---

## Dependency Graph

```
Phase 0 -> Phase 1 -+-> Phase 2 -> Phase 5 -> Phase 6 -> Phase 10
                     +-> Phase 3 -> Phase 4 -> Phase 8 -> Phase 9
                     +-> Phase 7 (independent, can parallel with 2-6)
                                    Phase 6 requires Phase 4 (UI wiring)
```

Tracks A (rendering), B (parsing/interpreter), and C (containers) can be developed in parallel after Phase 1.

---

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| WOBA decompression complexity | High | Line-by-line reference HyperCardPreview Swift decoder. Extensive unit tests |
| HFS B-tree traversal | High | Reference libfshfs docs + HFSExplorer Java source. Test against sample .img |
| StuffIt LZW implementation | Medium | Port from thecloudexpanse/sit C reference. LZW is well-documented |
| PICT opcode coverage | Medium | Implement only opcodes found in real stacks. Log unsupported |
| HyperTalk language breadth | Medium | Target sample stack commands first. Expand based on real-world testing |
| Font fidelity | Low | Bundle open-source Chicago/Geneva alternatives if licensing allows |

## Decisions

1. **Language:** C# 12 / .NET 8 (LTS)
2. **Container extraction:** All native C#, no external tool dependencies
3. **HC version support:** HC 2.x first, 1.x deferred
4. **Binary parsing:** Span<byte> + BinaryPrimitives (big-endian), no BinaryReader

## Verification

After each phase, verify by:
1. `dotnet build` — zero errors, zero warnings
2. `dotnet test` — all unit tests pass
3. `dotnet run --project src/HyperCardSharp.App` — app launches
4. Manual test: open `samples/NEUROBLAST_HyperCard` and verify expected behavior for that phase
5. After Phase 7: also test with `samples/NEUROBLAST_Cyberdelia.sit` and `samples/neuroblast.img`
