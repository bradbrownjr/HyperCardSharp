# HyperCard# — Full Implementation Plan

## Context

We are building a native, cross-platform HyperCard stack viewer in C# / .NET 8 that opens classic Mac HyperCard files directly — no Mac emulation. The project must handle stacks arriving as raw files, StuffIt archives, or disk images. It must support B&W and color display modes, embedded QuickTime MOV playback via LibVLC, and a HyperTalk script interpreter. The goal is to create a usable foundation that attracts community contributors interested in retro computing preservation.

**Target version: HyperCard 2.4.1.** This is the last version Apple shipped and the most widely distributed — the vast majority of community stacks were created with it. Full HC 2.4.1 compatibility is the primary correctness bar for every subsystem (parser, renderer, interpreter, container formats). HyperCard 1.x is a secondary target: detect the older format version and surface a warning; full 1.x support is deferred to Phase 15. Password-protected stacks (HC 2.4 XOR encryption) are out of scope for decryption — detect them and show a clear user message.

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

### Phase 10: Polish + Extended HyperTalk ✅ COMPLETE — tagged v0.1.0
- Extended HyperTalk: `repeat with/while/until`, `do`, `send`, string/math/date functions
- Chunk expressions: `char`, `word`, `item`, `line` of containers
- Visual effects: dissolve, wipe, iris, checkerboard (SkiaSharp transitions)
- Scrolling fields, Find command
- Menu bar, drag-and-drop, recent files
- Error display in status bar / log panel

---

## Post-v0.1.0 Phases

These phases address stubs and gaps identified during v0.1.0 development. Each is a discrete vertical slice of work.

### Phase 11: HyperTalk Runtime Completeness

Resolve all interpreter stubs so scripts behave as they would in real HyperCard.

- **`show` / `hide` commands** — toggle `Part.Visible` on the live card model and request a redraw (currently log-only)
- **`click at <x,y>` command** — hit-test parts at the given point and synthesise a `mouseUp` dispatch (currently log-only)
- **`type <text>` command** — append text to the focused field (currently log-only)
- **`wait <n> [ticks|seconds|milliseconds]`** — implement async delay using `Task.Delay` (currently skipped)
- **`send <msg> to <target>` command** — script is retrieved but never executed; wire `ExecuteHandler()` call after lookup
- **`set <property> of <part>` full coverage** — currently only `hilite`, `text`, `visible` work; add `name`, `rect`, `style`, `textFont`, `textSize`, `textStyle`, `enabled`
- **Field text mutation** — `SetFieldText` / `SetPartVisible` currently log "deferred"; make card/background part content mutable and trigger re-render
- **Button hilite read-back** — `GetButtonHilite` returns `null`; read from live part state

**Milestone:** All HyperTalk VM commands execute real behavior. Re-run NEUROBLAST — all script interactions work without log stubs. **Commit.**

### Phase 12: Styled Text Rendering

Styled text run data is parsed and stored but completely ignored during rendering.

- **`StyleTableBlock` + `FontTableBlock` lookup** — wire into `TextRenderer.DrawFieldText` and `TextRenderer.DrawButtonLabel`
- **Per-run font/size/style application** — apply `SKTypeface`, `TextSize`, bold/italic/underline per `StyleRun` span
- **Mac font ID → system font mapping** — extend `FontMapper` with Geneva (12/9pt special-case), Chicago, Monaco, Palatino, Times, Helvetica substitution table
- **Mixed-style layout** — measure each run individually; line-wrap across run boundaries; handle superscript/subscript offsets
- **`set textFont/textSize/textStyle of field`** — update style runs at runtime when set via HyperTalk

**Milestone:** Open a stack with styled fields — different fonts, sizes, bold/italic render correctly. **Commit.**

### Phase 13: Sound Playback via LibVLC

The `play` command callback exists but has a TODO — no audio output.

- **`SoundDecoder.cs`** — implement Mac `snd ` resource PCM extraction: parse sound header (format 1/2), extract 8-bit µ-law or raw PCM samples, write to a temp WAV or pipe
- **`MediaService.cs`** — LibVLCSharp wrapper: `PlayAudio(byte[] pcm, int sampleRate)` using `LibVLC.Media` from stream; also `PlayFile(string path)` for external MOV
- **Wire `PlaySound` callback** in `StackViewModel` — look up `snd ` resource by name in `StackFile.Resources`, decode, hand to `MediaService`
- **`stop sound` command** — add to interpreter and stop active media
- **MOV playback** — wire `VideoView` into the card area when a `play movie` command targets a rect

**Milestone:** `play "boing"` triggers the correct sampled sound. MOV resources play inline. **Commit.**

### Phase 14: AddColor / Color Rendering

Color mode currently returns the B&W bitmap unchanged — `AddColorDecoder` is a complete stub.

- **`AddColorDecoder.cs`** — parse `HCcd` (card color) and `HCbg` (background color) resources: 4-byte header, list of color regions (`partId`, `rect`, `fill color`, `frame color`)
- **`ColorRenderer.cs`** — apply color regions as filled rectangles composited over the B&W base layer using SkiaSharp `SKPaint` with `SKBlendMode.Multiply` / `SrcOver`
- **`AddColor` part-level color** — match `partId` to rendered part rect and tint button/field backgrounds
- **`PICT` resource rendering** — implement `PictDecoder.cs` covering the opcodes found in real stacks: `0x0001` ClipRect, `0x0011` VersionOp, `0x001E`/`0x001F` DefHilite, `0x0098` PackBitsRect, `0x009A` DirectBitsRect, `0x00FF` EndPic; log any unrecognised opcode rather than crashing
- **Resource fork → rendering pipeline integration** — ensure `StackFile` exposes color resources so `CardRenderer` can access them in Color mode

**Milestone:** Toggle to Color mode — AddColor stacks show filled color regions. PICT backgrounds render. **Commit.**

### Phase 15: Format Robustness

Gaps in format handling that cause silent failures or crashes on real-world stacks.

- **HyperCard 1.x stack support** — detect format version ≤ 7 in `StackBlock`; map the different field offsets for card count, card size, and list block location; log a warning for any 1.x-only feature
- **Mac Roman encoding** — replace Latin-1 proxy in `PartContent.cs` with a proper Mac Roman → UTF-16 lookup table (characters 0x80–0xFF differ)
- **`go home` / Home stack** — resolve to a configured home stack path or open the file picker rather than logging a no-op
- **Unknown block tracing** — ensure every unknown block type logs its 4-char type code, byte offset, and size (already partial; audit and harden)
- **Large stack stability** — test with stacks > 100 cards; verify `LIST`/`PAGE` B-tree walk handles multi-page card lists without index errors
- **Password-protected stacks** — detect encrypted STAK flag; surface a readable "This stack is password-protected" message instead of garbage rendering

**Milestone:** Open a diverse set of real-world stacks without crashes. 1.x stacks display a version warning rather than corrupting. **Commit.**

### Phase 16: Community Release Readiness

Non-functional work to make the project welcoming to contributors and end-users.

- **README.md** — feature overview, screenshots, download instructions, contributor guide, link to `docs/`
- **`docs/hypertalk-coverage.md`** — table of every HyperTalk command and function with ✅/⚠️/❌ status
- **`docs/stack-format.md`** — update with all block types encountered, field offsets confirmed against real stacks
- **GitHub Issues** — file issues for each Phase 11–15 stub so the community can contribute
- **CI workflow** — `.github/workflows/build.yml`: `dotnet build` + `dotnet test` on push/PR for Windows, macOS, Linux
- **Self-contained publish** — verify `dotnet publish -r win-x64 --self-contained` produces a single-folder app with LibVLC bundled
- **App icon + About dialog polish** — replace placeholder icon; About dialog shows version from assembly metadata

**Milestone:** Project is publicly presentable. CI is green. Single-folder redistributable builds for all three platforms. **Commit + tag v0.2.0.**

**Milestone:** Polished, usable viewer for community release. **Commit + tag v0.1.0.**

---

## Post-v0.2.0 Phases — HyperTalk Full Functionality

These phases close the remaining gaps in `docs/hypertalk-coverage.md` to achieve full HyperCard 2.4.1 script compatibility.

### Phase 17: System Messages & Idle Loop

HyperCard delivered a steady stream of system messages that scripts could intercept. Many stacks depend on these for animation, rollovers, and auto-advance.

- **`idle` message** — fire on a timer (every ~100 ms) when no user interaction is pending; dispatch through card → background → stack hierarchy
- **`mouseEnter` / `mouseLeave`** — track mouse position over parts; fire on enter/leave; enables rollover highlight stacks
- **`mouseWithin`** — fire repeatedly while mouse stays inside a part
- **`keyDown` / `tabKey` / `returnKey` / `enterKey` / `arrowKey`** — dispatch keyboard events as system messages with key value in `the paramList`
- **`newCard` / `deleteCard`** — fire on card creation/deletion (player mode: `newCard` is informational)
- **`suspendStack` / `resumeStack`** — fire on window focus changes
- **Mouse/keyboard properties** — implement `the mouse`, `the mouseH`, `the mouseV`, `the mouseClick`, `the clickLoc`, `the key`, `the keyCode` as live-read properties from the UI layer

**Milestone:** `idle`-based animation stacks auto-advance. `mouseEnter`/`mouseLeave` stacks highlight on hover. Keyboard messages dispatch. **Commit.**

### Phase 18: Stack-Level Script & Message Hierarchy Completion

The message hierarchy is incomplete — the stack script itself and the HyperCard level are missing.

- **Stack-level script** — parse the STAK block's script field; add it to the message dispatch chain after background and before HyperCard
- **HyperCard-level handlers** — implement a built-in "Home stack" script context at the top of the hierarchy (after stack); stub common Home handlers like `doMenu`
- **`pass` semantics** — verify `pass <handler>` correctly bubbles through all five levels: part → card → background → stack → HyperCard
- **`the target`** — return the part that originally received the message
- **`the params` / `the paramList`** — return handler arguments as passed

**Milestone:** Stacks with handlers in the stack script (not just card/background) work correctly. Full 5-level message hierarchy operational. **Commit.**

### Phase 19: Nested Chunk Expressions & Container Improvements

Chunk expressions are the string-processing backbone of HyperTalk. Nested chunks are used in almost every non-trivial stack.

- **Nested chunk reads** — `word 2 of line 3 of field "data"` evaluates inner-to-outer; implement recursive chunk resolution in expression evaluator
- **Nested chunk writes** — `put "x" into word 2 of line 3 of myVar` reconstructs the string with the inner chunk replaced
- **`the number of` chunks** — `the number of words of line 3 of x`; chain with nested chunks
- **Range chunk writes** — `put "x" into char 3 to 5 of line 2 of myVar`
- **`last` / `any` / `middle` chunk qualifiers** — `last word of x`, `any line of x`, `middle char of x`
- **`the message box`** — implement as a special container; `put x into message` shows a message log/text area

**Milestone:** Complex chunk expressions like `word 2 of line 3 of field "data"` evaluate and assign correctly. **Commit.**

### Phase 20: Property System Completion

Many stacks read and write part properties dynamically. Property read-back has several gaps.

- **`visible of part` read-back** — return actual `Part.Visible` state instead of always `true`
- **`the result`** — set to the outcome of the last command (`empty` on success, error message on failure); particularly important for `find`, `go`, `play`
- **`answer` button read-back** — set `it` to the label of the button the user clicked in `answer` dialogs
- **`set script of` / `set cursor to` / `set userLevel to`** — implement or stub remaining `set` targets
- **`the screenRect`** — return the rendering area dimensions
- **`the tool`** — return `"browse"` (player mode only)
- **`the userLevel`** — return `5` (scripting level, read-only in player)
- **`text of card`** — concatenate all field text on the current card
- **`hilite of button` read-back** — ensure bidirectional: `set` and `get` both work on live state

**Milestone:** Property-dependent stacks (especially those that read `the result` or `visible`) work correctly. **Commit.**

### Phase 21: Find Command Completion & Text Search

The `find` command is central to many educational and reference stacks.

- **`find "text" in field X`** — parse `in field` scope clause; restrict search to named field
- **Find mode qualifiers** — implement `find whole`, `find chars`, `find word`, `find string` with distinct matching semantics (whole word, substring, word-start, exact)
- **Find highlight** — visually highlight the found text on the card (box around matching text, as original HC did)
- **`find` across backgrounds** — search continues across background boundaries
- **`the foundText` / `the foundChunk` / `the foundLine` / `the foundField`** — properties that report what `find` matched

**Milestone:** `find whole "mitochondria" in field "glossary"` works with correct scoping and visual highlight. **Commit.**

### Phase 22: XCMD/XFCN Integration

XCMDs (external commands) and XFCNs (external functions) were HyperCard's plugin system. Many stacks used common XCMDs for color, sound, and file access.

- **XCMD registry wiring** — connect the existing `XcmdRegistry` to the interpreter so `ExecCommand` falls through to registered handlers before logging "unknown command"
- **Built-in XCMD emulations** — implement common XCMDs as native C# handlers:
  - `AddColor` — already handled via rendering; register name so scripts expecting a response get one
  - `Flash` — briefly invert the screen/card region
  - `ResCopy` / `GetResource` — resource fork access (return resource data or empty)
  - `FileName` / `FileIO` — basic file dialog / read-only file access (sandboxed)
  - `Palette` — color palette window (stub with log)
- **XFCN return values** — ensure XFCNs can return a string value via `it` or `the result`
- **Unknown XCMD handling** — graceful "XCMD not available" dialog matching original HC behavior

**Milestone:** Stacks that call common XCMDs get reasonable behavior instead of silent no-ops. **Commit.**

### Phase 23: File I/O & Miscellaneous Commands

Remaining HyperTalk commands that real stacks use.

- **`open file` / `close file` / `read from file` / `write to file`** — sandboxed file I/O (read-only by default; write requires user confirmation)
- **`show cards` / `show all cards`** — rapid flip-through of all cards (slideshow mode)
- **`drag from <point> to <point>`** — synthesize mouse-drag events
- **`choose <tool>`** — log or ignore in player mode; some stacks test `the tool`
- **`print card`** — render current card to printer or PDF
- **`doMenu <menuItem>`** — handle common menu commands (`New Card`, `Delete Card`, `Copy`, `Paste`, etc.) or log unsupported
- **Remaining math functions** — `atan()`, `exp2()`, `annuity()`, `compound()`

**Milestone:** File-reading stacks, slideshow stacks, and stacks using `doMenu` work. **Commit.**

### Phase 24: Real-World Stack Test Suite

Validate all previous work against a broad corpus of real stacks.

- **Stack test harness** — automated loader that opens each stack in `samples/`, navigates all cards, verifies no exceptions
- **Script execution smoke tests** — extract scripts from sample stacks, parse and interpret each, verify no unhandled errors
- **Rendering regression tests** — render first/last card of each sample stack, compare against golden reference bitmaps (pixel hash comparison)
- **Coverage report** — log every unrecognized command, unknown block type, and missing property across the full test corpus; feed results back into gap tracking
- **Community stack corpus** — curated collection of diverse stacks: educational, games, multimedia, database, with notes on expected behavior

**Milestone:** 90%+ of community stacks load and navigate without errors. Known gaps documented per-stack. **Commit + tag v0.3.0.**

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
3. **HC version support:** HyperCard 2.4.1 is the explicit target. HC 2.x ≥ 2.0 should work. HC 1.x: detect and warn (Phase 15). Encryption: detect and report, no decryption.
4. **Binary parsing:** Span<byte> + BinaryPrimitives (big-endian), no BinaryReader

## Verification

After each phase, verify by:
1. `dotnet build` — zero errors, zero warnings
2. `dotnet test` — all unit tests pass
3. `dotnet run --project src/HyperCardSharp.App` — app launches
4. Manual test: open `samples/NEUROBLAST_HyperCard` and verify expected behavior for that phase
5. After Phase 7: also test with `samples/NEUROBLAST_Cyberdelia.sit` and `samples/neuroblast.img`
