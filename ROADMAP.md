# HyperCard# Roadmap

This document is the durable, session-surviving plan for HyperCard#. It records
what has been diagnosed, what to build, in what order, and which class of AI
model (or human) each task suits. It supersedes `docs/PLAN.md` (kept for
history).

**How to use this document.** Each task has an ID (A1, B3...), a goal, the
files involved, an explicit spec, acceptance criteria, and a model
recommendation. Work tasks in phase order unless marked independent. When a
task is finished, change its status line, note the commit hash, and keep the
spec text intact so later readers understand what was done and why. Never mark
a phase complete while any task in it is `pending` (see the Phase Completion
Guardrail in `AGENTS.md`).

**Model routing guide.**

| Model | Use for | Rule of thumb |
|-------|---------|---------------|
| haiku | Mechanical, fully-specified, single-file edits with a clear pass/fail test | The spec below contains every constant and line reference needed; no design decisions remain |
| sonnet | Multi-file feature work following an explicit spec or a named reference implementation | Design is settled here; the task is careful implementation and testing |
| opus | Cross-cutting design, porting from foreign-language references, debugging without a known cause | Judgment calls remain; the spec defines the goal and constraints, not the shape |
| fable | Format reverse engineering with sparse references, architectural rewrites, anything touching many modules at once | The task may invalidate assumptions elsewhere in the codebase |

A larger model can always take a smaller model's task. The reverse is the risk.

---

## 1. Current state assessment (diagnosed 2026-07-11)

Verified by building the solution and rendering sample stacks headless to PNG.

**What works and should not be rewritten:**

- WOBA bitmap decompression (`src/HyperCardSharp.Core/Bitmap/WobaDecoder.cs`)
  is pixel-accurate. NEUROBLAST (70 cards, 640x400) and Cyberdelia (55 cards,
  512x342) render their dithered artwork correctly.
- Block parsing (STAK/MAST/LIST/PAGE/CARD/BKGD/BMAP/STBL/FTBL) is structurally
  correct against HC 2.x stacks.
- The HyperTalk lexer/parser/interpreter skeleton covers a usable statement
  set (if/repeat/put/set/go/answer/ask/send/do/show/hide/visual and more).
- The StuffIt method-13 (LZSS+Huffman) decompressor works when invoked
  directly (verified against `NEUROBLAST_Cyberdelia.sit`, which yields a valid
  1,040,384-byte STAK).

**Commit-history lesson.** Of 33 commits, the last ~15 fought window chrome
(menu bar, Apple logo, Windows transparency launch failures) while the card
canvas itself had the defects below. Chrome fidelity remains a goal (Phase E)
but is sequenced after in-card fidelity, because the preservation mission
lives inside the 512x342 rectangle.

### Findings ledger

Each finding lists evidence. Verify the code still matches before fixing;
line numbers drift.

- **F1. Parts are never rendered.** `PartRenderer.cs` (header comment, lines
  ~10-13) assumes button/field chrome is baked into the WOBA bitmap. It is
  not. HyperCard drew all part chrome live from part properties: button
  frames, titles, icons, checkboxes, radio buttons, hilite states, field
  borders, text baselines, scrollbars. Today only field text is drawn,
  floating over the paint layer. This is the single largest fidelity defect.
- **F2. No message-passing hierarchy.** `StackViewModel.HandleCardClick`
  (`src/HyperCardSharp.App/ViewModels/StackViewModel.cs`, ~line 101)
  dispatches `mouseUp` only to the clicked part's own script and returns if
  the script is empty. Real HyperCard passes unhandled messages part -> card
  -> background -> stack. Most stacks keep navigation in background scripts
  with empty-scripted buttons; those are all dead. System messages
  (`openStack`, `openCard`, `closeCard`, `mouseDown`) are never sent at all.
  This, plus F1, is the historical "clickable regions don't render and don't
  click" problem.
- **F3. All .sit extraction silently fails.**
  `StuffItExtractor.cs` (~line 80) calls `Encoding.GetEncoding("macintosh")`,
  which always throws on .NET 8 because `CodePagesEncodingProvider` is never
  registered anywhere in the solution. A blanket `catch` (~line 126) swallows
  the exception and returns an empty result, which the UI reports as
  "found no STAK entries". One registration line plus one NuGet package fixes
  every classic .sit file whose compression methods we support.
- **F4. The HFS reader has never executed.** `HfsReader.cs` (~line 15) and
  `HfsExtractor.cs` (~line 11) define `HfsMdbSignature = 0xD2D7`. That value
  is the MFS (400K floppy) signature. Classic HFS is `0x4244` (ASCII "BD"),
  confirmed present at offset 1024 in both sample .img files. Because
  detection never matches, .img files fall through to `RawStackScanner`,
  which loses file names, resource forks, and multi-stack enumeration
  (stacks show as "Untitled").
- **F5. WOBA mask layer decoded then discarded.** `WobaDecoder.cs` compose
  step (~lines 46-60) computes the mask, then outputs only the image layer
  with a comment "Full composition will be handled at render time" (it is
  not). `CardRenderer` then draws the card bitmap fully opaque, so any stack
  whose background holds the artwork gets whited out by the card layer. The
  samples hid this because their art is card-level and full-bleed.
- **F6. Resource forks are discarded by the container pipeline.**
  `IContainerExtractor.Extract` returns a single `byte[]` (data fork). Icons
  (`ICON`, `cicn`), sounds (`snd `), `PICT`s, embedded fonts (`FOND`/`NFNT`),
  and AddColor (`HCcd`/`HCbg`) all live in resource forks. Example casualty:
  `BeavisEmulatorV2.sit` carries 233 KB in its resource fork. Nothing
  fork-dependent can ever work until the pipeline model carries both forks.
- **F7. Text decoded as Latin1, not MacRoman.** `Part.cs`, `CardBlock.cs`,
  `BackgroundBlock.cs`, `PartContent.cs` (`ReadMacRoman` is a Latin1 stub).
  Curly quotes, bullets, dashes, and all accented characters render wrong.
  Same root fix as F3.
- **F8. Styled text runs parsed but unused.** `PartContent` collects
  `StyleRun`s and `StyleTableBlock`/`FontTableBlock` parse correctly, but
  `TextRenderer` draws each field with the part-level font only.
- **F9. Fonts substitute Arial/Courier New.** `FontMapper.cs` maps classic
  font IDs to modern vector fonts with antialiased metrics. Authentic
  rendering needs pixel-accurate classic fonts (see Asset Policy) drawn with
  antialiasing off, at integer scales.
- **F10. Unsupported container variants crash or fail opaquely.**
  `ContextualMenus.sit` is StuffIt 5 format (`StuffIt (c)1997-` magic), not
  classic `SIT!`; it is misrouted to `StackParser`, which throws
  `ArgumentOutOfRangeException` (violates the never-crash rule).
  `BeavisEmulatorV2.sit` is classic SIT! but uses compression method 5
  (LZAH), which is unimplemented.

---

## 2. Asset and legal policy

Researched 2026-07-11. Classic Mac OS and HyperCard are **not** public domain
and were never released as freeware in a legally durable sense. Apple made
some old system software freely downloadable years ago, but copyright was
retained. "Abandonware" is a community label with no legal force.

Policy for this project:

1. **Bundle nothing extracted from Apple software.** No Chicago/Geneva NFNT
   bitmaps ripped from a System file, no HyperCard built-in icon bitmaps, no
   `snd` resources, no PICT clip art.
2. **Render everything the user's own files contain.** Stacks and disk images
   supplied by the user often embed the fonts, icons, sounds, and color
   resources they need. Decoding and rendering user-supplied data is the
   product's core function and is the user's responsibility, matching how
   emulator-based preservation tools operate.
3. **Use open recreations for defaults.** When a stack references a resource
   it does not contain (for example HyperCard's built-in icons, or Chicago as
   the system font), fall back in this order: (a) an open-licensed recreation
   bundled with the app, (b) an original placeholder drawn by this project,
   (c) a logged graceful degradation. Known-good recreations:
   - ChicagoFLF: public domain, by Robin Casady.
   - Urban Renewal set (Kreative Korp): TrueType recreations of the classic
     city fonts; verify current license text before bundling.
   - ChiKareGo2: free Chicago recreation; verify license before bundling.
4. **Built-in icon IDs.** HyperCard stacks reference standard icons by ID
   that live inside HyperCard itself. Maintain a table of well-known IDs and
   map them to original, from-scratch replacement pixel art contributed to
   this repo (drawn without copying Apple bitmaps). Until replacements exist,
   draw a neutral 32x32 placeholder frame and log the missing ID.

---

## 3. Modularity and documentation charter

Goal: any single task in this roadmap can be executed by a small model or a
new human contributor without holding the whole system in their head.

**Module boundaries (enforced by project references, no cycles):**

- `Core` knows bytes and models. It never references SkiaSharp, Avalonia, or
  the interpreter.
- `Rendering` knows Core models and SkiaSharp. It never references Avalonia.
- `HyperTalk` knows Core models (for object resolution) and nothing visual.
- `App` composes the other three; all Avalonia types stay here.
- New: `Core/Resources/` owns resource-fork decoding (fonts, icons, sounds,
  color). New: `App/Chrome/` owns all System 7 look-alike window furniture.

**Code rules:**

- One binary block type, one file. One decoder, one file.
- Byte-offset knowledge lives inline: every parser documents field offsets
  and sizes next to the reads (existing style in `CardBlock.cs` is the
  model to follow; keep it).
- Public APIs carry XML doc comments stating units, coordinate systems
  (HyperCard rects are top,left,bottom,right in card coordinates), and
  endianness assumptions.
- Files should stay under ~400 lines; split by responsibility when they
  grow past that.
- Every project gets a `README.md` (one screen) stating what the project
  owns, what it must not reference, and where its tests live.
- No emoji, no em-dashes in code or docs (house style).

**Documentation debt to clear:** `AGENTS.md` references
`docs/stack-format.md` and `docs/hypertalk-coverage.md`, which do not exist.
Task A6 creates them; every later task that learns format facts appends to
them.

---

## 4. Phase A: Every sample opens correctly

Small, high-confidence fixes. Do these first; they unblock all later testing.

### A0. Commit the render-dump harness as a tool  [model: sonnet] [status: pending]

- **Goal:** A repeatable CLI that renders any stack's cards to PNGs, so every
  later rendering task can show before/after evidence, and CI can diff
  golden images.
- **Files:** new `tools/RenderDump/RenderDump.csproj`, `Program.cs`; solution
  file; `README.md` in the tool folder.
- **Spec:** Console app referencing Core and Rendering. Args:
  `<input-file> <out-dir> [max-cards]`. Pipeline: read file,
  `ContainerPipeline.UnwrapMultiple`, `new StackParser().Parse`, iterate
  cards in `GetCardOrder()` order, `CardRenderer.RenderCard`, encode PNG via
  `SKImage.Encode`. Print per-card part/content counts and all pipeline log
  lines. Non-Windows CI/dev needs package
  `SkiaSharp.NativeAssets.Linux.NoDependencies` version-matched to SkiaSharp.
  Also add a `--info` mode that prints the block inventory and per-card part
  tables (id, type, style, rect, name, icon id, script length) without
  rendering.
- **Accept:** `dotnet run --project tools/RenderDump -- samples/NEUROBLAST_HyperCard out 6`
  writes 6 PNGs on Linux, macOS, Windows.

### A1. Register MacRoman encoding once, use it everywhere  [model: haiku] [status: pending]

- **Goal:** Fix F3 and F7 with one mechanism.
- **Files:** `src/HyperCardSharp.Core/HyperCardSharp.Core.csproj`; new
  `src/HyperCardSharp.Core/Binary/MacText.cs`; then replace call sites in
  `StuffItExtractor.cs`, `PartContent.cs`, `Part.cs`, `CardBlock.cs`,
  `BackgroundBlock.cs`, and any other `Encoding.Latin1` / `GetEncoding("macintosh")`
  use found by `grep -rn "Latin1\|macintosh" src/`.
- **Spec:** Add NuGet `System.Text.Encoding.CodePages` to Core. Create
  `public static class MacText` with a static constructor calling
  `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` and a
  cached `Encoding MacRoman = Encoding.GetEncoding(10000)`. Expose
  `string Decode(ReadOnlySpan<byte> bytes)` and
  `string DecodeNullTerminated(ReadOnlySpan<byte> data, int offset)`
  (consolidating the three duplicate `ReadNullTerminatedString` helpers,
  DRY). Replace every Latin1/"macintosh" text decode with `MacText`.
- **Accept:** New unit test: bytes `0xD2 0xD3 0xA5` decode to
  U+201C U+201D U+2022 (curly quotes, bullet). Existing tests still pass.

### A2. Fix the HFS signature constant and classic-HFS enumeration  [model: haiku] [status: pending]

- **Goal:** Fix F4 so the 391-line HFS reader actually runs.
- **Files:** `src/HyperCardSharp.Core/Containers/HfsReader.cs`,
  `HfsExtractor.cs`, `ContainerPipeline.cs`.
- **Spec:** Change `HfsMdbSignature` from `0xD2D7` to `0x4244` in both files.
  In `ContainerPipeline.UnwrapMultiple`, the `RawStackScanner` pre-check
  currently probes only HFS+ (`HfsPlusReader.IsHfsPlus`); add the classic
  path: construct `HfsReader`, if `IsHfs()` call `EnumerateStacks()` and
  return named results when non-empty, before falling through to raw scan.
- **Accept:** `RenderDump samples/NEUROBLAST_Cyberdelia.img` logs an HFS
  extraction (not RawStackScanner) and reports the stack's real HFS file
  name, not "Untitled". Both sample .img files still open.

### A3. Verify .sit extraction end to end after A1  [model: haiku] [status: pending]

- **Goal:** Confirm F3's fix restores the classic StuffIt path.
- **Files:** none expected beyond a regression test in
  `tests/HyperCardSharp.Core.Tests/`.
- **Spec:** Add a test (skippable when `samples/` absent) asserting
  `new StuffItExtractor().ExtractAll(File.ReadAllBytes("samples/NEUROBLAST_Cyberdelia.sit"))`
  returns one entry named `NEUROBLAST_Cyberdelia` whose bytes have STAK magic
  at offset 4.
- **Accept:** Test passes; RenderDump renders cards from the .sit directly.

### A4. Never crash on unrecognized input  [model: sonnet] [status: pending]

- **Goal:** Fix the F10 crash path; enforce the graceful-degradation UX rule.
- **Files:** `src/HyperCardSharp.Core/Stack/StackParser.cs`,
  `src/HyperCardSharp.Core/Binary/MagicDetector.cs`,
  `src/HyperCardSharp.App/ViewModels/StackViewModel.cs`.
- **Spec:** `StackParser.Parse` and `EnumerateBlocks` must validate block
  sizes/offsets before slicing (reject size < 16, size beyond EOF, offset
  overflow) and throw a single typed `StackFormatException` with offset
  context instead of `ArgumentOutOfRangeException`. `MagicDetector` learns
  the StuffIt 5 magic (`"StuffIt (c)1997-"` prefix at offset 0) so SIT5
  files are *identified* and produce the message "StuffIt 5 archives are not
  supported yet" rather than being fed to the stack parser. App catches
  `StackFormatException` and shows a readable status message.
- **Accept:** Opening `samples/ContextualMenus.sit` shows a friendly
  unsupported-format message; opening a random binary shows "not a HyperCard
  stack"; no exception escapes to a crash in either case.

### A5. Preserve resource forks in the container pipeline  [model: opus] [status: pending]

- **Goal:** Fix F6. This is the architectural prerequisite for icons, sounds,
  fonts, PICT, and AddColor (Phase D), so do it while touching containers.
- **Files:** `src/HyperCardSharp.Core/Containers/*` (interface and all
  extractors), `ContainerPipeline.cs`, `StackFile.cs`, callers in App and
  tests.
- **Spec:** Replace the `(string Name, byte[] Data)` currency with a record
  `ExtractedFile { string Name; byte[] DataFork; byte[]? ResourceFork; string? Type; string? Creator; }`.
  MacBinary, AppleSingle, BinHex, StuffIt, and HFS all already parse both
  forks internally or can trivially expose them; DiskCopy yields a volume
  (unchanged); RawStackScanner yields DataFork only. `StackFile` gains
  `ResourceFork` bytes (nullable). Keep `Unwrap`'s recursive re-detection
  behavior: the *data fork* is what gets re-detected for nesting; the
  resource fork rides along with the final result. Update
  `docs/stack-format.md` with the fork-carrying pipeline diagram.
- **Accept:** Loading `BeavisEmulatorV2.sit` (after D5 implements LZAH it
  will fully extract; until then use `NEUROBLAST_Cyberdelia.sit`) exposes a
  non-null resource fork on the loaded stack; existing single-fork behavior
  unchanged for raw stacks.

### A6. Create the missing research docs  [model: haiku] [status: pending]

- **Goal:** Make `docs/stack-format.md` and `docs/hypertalk-coverage.md`
  exist so later tasks have a place to record findings (AGENTS.md already
  references both).
- **Spec:** `stack-format.md`: seed with the block map (STAK, MAST, LIST,
  PAGE, CARD, BKGD, BMAP, STBL, FTBL, PRNT, PRST, PRFT, TAIL), the CARD/BKGD
  layouts already documented in code comments, the part-entry layout from
  `Part.cs`, WOBA notes from `WobaDecoder.cs`, and a "container formats"
  section (SIT classic entry layout incl. compression method table: 0 none,
  1 RLE90, 2 LZW, 3 Huffman, 5 LZAH, 13 LZSS+Huffman, 15 Arsenic; SIT5
  detection magic; HFS MDB `0x4244` at offset 1024 vs MFS `0xD2D7`).
  `hypertalk-coverage.md`: table of statements/functions/properties with
  status (done/partial/missing) generated by reading
  `HyperTalkInterpreter.cs` and `AstNodes.cs`.
- **Accept:** Both files exist, are linked from README, and match the code.

**Phase A exit criteria:** 5 of 6 sample files open with correct names via
their intended container path (ContextualMenus.sit correctly reports SIT5
unsupported until D6). `dotnet test` green. Docs updated.

---

## 5. Phase B: Faithful card rendering

The big fidelity jump. B1 is the keystone; B2-B7 build on it.
Reference implementation throughout: HyperCardPreview (Swift, GPL,
https://github.com/PierreLorenzi/HyperCardPreview), especially its part
drawing and text layout. Use it as a *specification* (read, understand,
re-express); do not translate GPL code line by line into this MIT project.

### B1. Carry the WOBA mask through to compositing  [model: sonnet] [status: pending]

- **Goal:** Fix F5: true three-layer compositing
  (background paint -> background parts -> card paint -> card parts), where
  card paint is transparent outside its mask.
- **Files:** `Bitmap/BitmapImage.cs`, `Bitmap/WobaDecoder.cs`,
  `Rendering/BitmapRenderer.cs`, `Rendering/CardRenderer.cs`.
- **Spec:** `BitmapImage` gains `byte[] Mask` (same row layout as `Data`).
  WOBA semantics (per HyperCardPreview and the Wildfire format doc): a pixel
  is *black* if the image bit is 1; *opaque white* if the mask bit is 1 and
  image bit is 0; *transparent* if both are 0. When `MaskDataSize == 0` and
  `MaskRect` is empty, the image is self-masking (only image=1 pixels are
  opaque black). When `MaskDataSize == 0` but `MaskRect` is non-empty, the
  whole `MaskRect` is opaque white. `BitmapRenderer` gets one method
  producing BGRA with those three states (replace the two current
  variants, DRY). `CardRenderer` draws background bitmap opaque (white base),
  then card bitmap with transparency. Respect `CardBlock.HideCardPicture`
  and the equivalent background flag: skip the layer when set.
- **Accept:** Golden-image test: hand-build a 16x16 BMAP fixture with known
  mask/image bits and assert the composited RGBA output; NEUROBLAST renders
  unchanged (its cards are self-contained); a stack whose background carries
  art no longer gets whited out (add such a fixture stack or craft one).

### B2. Render button and field chrome  [model: opus] [status: pending]

- **Goal:** Fix F1. Draw every part style the way HyperCard did.
- **Files:** `Rendering/PartRenderer.cs` (rewrite; delete the "baked into
  WOBA" comment and logic), new `Rendering/PartChrome.cs` if helpful;
  `Core/Parts/PartStyle.cs` (verify the enum against the format doc:
  0 transparent, 1 opaque, 2 rectangle, 3 roundRect, 4 shadow, 5 checkBox,
  6 radioButton, 7 scrolling, 8 standard, 9 default, 10 oval, 11 popup).
- **Spec (buttons):** For each visible button, in part order (parts list
  order is z-order, first = bottom): transparent (no fill, no frame), opaque
  (white fill), rectangle (white fill + 1px black frame), roundRect (white
  fill, 1px black frame, corner radius 8, plus 1px drop shadow right+bottom
  offset 1), shadow (rect + 2px offset shadow), standard/default (roundRect
  look; default additionally gets the 3px rounded outer ring), oval
  (ellipse), checkBox (12x12 box left-aligned, x mark when hilited),
  radioButton (12x12 circle, filled dot when hilited), popup (rect with
  drop-down arrow and title-width label region). If `ShowName` (flag bit:
  `MoreFlags & 0x80` per format doc; verify) draw the name centered in the
  part's text font. If hilited (`Flags & 0x01`... verify against doc) invert
  the part rect (SkiaSharp: draw with `SKBlendMode.Difference` white fill,
  or render to layer and invert). Icon: if `IconIdOrFirstSelectedLine != 0`,
  draw the 32x32 icon centered above the name (D2 supplies real icons; until
  then draw the placeholder per Asset Policy 4).
- **Spec (fields):** transparent (text only), opaque (white fill), rectangle
  (fill + frame), shadow (fill + frame + offset shadow), scrolling (frame +
  16px scrollbar gutter with up/down arrow boxes, non-functional until C4).
  If `ShowLines` flag set, draw dotted baselines every `TextHeight` pixels.
  `WideMargins` adds 4px insets.
- **Accept:** RenderDump `--info` cross-checked against renders for a stack
  with visible buttons (craft `tests/fixtures/parts-showcase.stack` via a
  builder utility, or source a suitable freely-shareable stack and document
  it in docs/samples.md); every style enum value has a golden image test.
  NEUROBLAST/Cyberdelia unchanged except where transparent buttons with
  names now show them (verify against HyperCard screenshots on archive.org).

### B3. Pixel-authentic typography  [model: sonnet] [status: pending]

- **Goal:** Fix F9. Text looks like a Mac, not like Arial.
- **Files:** `Rendering/FontMapper.cs`, `Rendering/TextRenderer.cs`, new
  `assets/fonts/` with licenses; App resource embedding.
- **Spec:** Bundle open recreations per Asset Policy 3 (start with
  ChicagoFLF, public domain; evaluate Urban Renewal for Geneva/Monaco/New
  York and record license verification results in `assets/fonts/README.md`).
  `FontMapper` maps classic IDs (0 Chicago, 1 application=Geneva, 2 New
  York, 3 Geneva, 4 Monaco, 5 Venice, 6 London, 7 Athens, 8 San Francisco,
  9 Toronto, 11 Cairo, 12 Los Angeles, 14 Bookman, 16 Palatino, 18 Zapf
  Chancery, 20 Times, 21 Helvetica, 22 Courier, 23 Symbol, 24 Mobile) to
  bundled families first, system fallbacks second, logging substitutions.
  Load via `SKTypeface.FromStream` from embedded resources; cache per
  (family, style). All text drawing: `SKFont.Edging = Alias`,
  `Subpixel = false`, `Hinting = None`; scale by drawing the whole card at
  1x and letting the display control integer-scale with nearest-neighbor
  (`SKSamplingOptions(SKFilterMode.Nearest)`); verify the App's
  `SkiaBitmapControl` uses nearest-neighbor when zoomed.
- **Accept:** Field text in golden images is pixel-crisp (no gray
  antialiasing pixels) at 1x and 2x; Chicago-mapped text measurably matches
  classic metrics (12pt Chicago cap height 9px).

### B4. Styled text runs  [model: sonnet] [status: pending]

- **Goal:** Fix F8. Fields honor per-run font/size/style from STBL.
- **Files:** `Rendering/TextRenderer.cs`, `Rendering/CardRenderer.cs` (pass
  `StackFile.StyleTable` down), `Core/Stack/StyleTableBlock.cs` (add lookup
  by style id if missing).
- **Spec:** For a `PartContent` with `StyleRuns`, split text at
  `CharacterPosition` boundaries; each run resolves its `StyleId` in the
  style table; -1 fields inherit from the part's defaults. Draw runs
  sequentially with correct advances; line height per line = max run height
  unless part `TextHeight` fixed. Word wrap must operate on the styled
  segments (wrap by measured width across mixed runs).
- **Accept:** Unit test with a synthetic two-run content (plain + bold)
  asserts split positions and fonts; a real styled stack renders visibly
  mixed styles (add fixture; NEUROBLAST card 1 has 1 content entry to
  inspect for runs).

### B5. Correct visibility, z-order, and layer flags  [model: haiku] [status: pending]

- **Goal:** Small correctness sweep feeding B2.
- **Files:** `Core/Parts/Part.cs`, `Rendering/PartRenderer.cs`.
- **Spec:** Verify against the Wildfire format doc and record in
  `docs/stack-format.md`: `Flags` bit 7 = hidden (current `Visible` reads
  `(Flags & 0x80) == 0`; confirm), bit 0 = hilite for buttons / lockText for
  fields; `MoreFlags` bits: showName, autoHilite, sharedHilite/autoSelect,
  and the family number in the low nibble... document each with a test.
  Ensure background parts draw before card parts and parts draw in list
  order.
- **Accept:** `docs/stack-format.md` gains a verified flag table with a unit
  test per flag against fixture bytes.

### B6. B&W mode is the default truth; color is additive  [model: haiku] [status: pending]

- **Goal:** Keep the render path honestly 1-bit until AddColor lands.
- **Spec:** `RenderMode.BlackAndWhite` must produce strictly 2-color output
  (assert in a test: every pixel pure black or pure white after B2/B3).
  `RenderMode.Color` currently equals B&W; make the View menu label say
  "Color (requires AddColor, not yet supported)" grayed out until D4.
- **Accept:** Pixel-purity test green; menu reflects reality.

### B7. Golden-image regression suite  [model: sonnet] [status: pending]

- **Goal:** Lock in fidelity so later work cannot silently regress it.
- **Files:** `tests/HyperCardSharp.Rendering.Tests/Golden/`,
  CI workflow update.
- **Spec:** Test helper renders named fixture cards and compares PNG bytes
  to committed golden files with a small per-pixel tolerance (0 for 1-bit
  mode). Provide `UPDATE_GOLDENS=1` env-var path to regenerate. Fixtures:
  synthetic part-showcase stack (from B2), one NEUROBLAST card, one
  Cyberdelia card (samples are gitignored, so gate those two on file
  presence and run them in CI via the download script in docs/samples.md).
- **Accept:** CI fails when a rendering change alters goldens without
  regeneration.

**Phase B exit criteria:** A stack with standard buttons/fields is visually
indistinguishable from a period screenshot at 1x, except for resources the
file does not contain. All golden tests green.

---

## 6. Phase C: Faithful behavior

### C1. Message-passing hierarchy  [model: opus] [status: pending]

- **Goal:** Fix F2 properly (this is the root cause of dead clicks).
- **Files:** `HyperTalk/MessagePassing/MessageDispatcher.cs`,
  `App/ViewModels/StackViewModel.cs`, `HyperTalk/Interpreter/*` (the
  interpreter needs a "current object" context for `me`/`target`).
- **Spec:** Introduce `MessageTarget` chain built per event:
  clicked part -> its card -> the card's background -> the stack. Dispatch
  walks the chain: first script containing a matching handler runs; a
  handler that executes `pass <message>` continues the walk; absence of a
  handler falls through silently. Unhandled built-in messages do nothing;
  unhandled *command* messages surface "can't understand" to the message
  log. Wire system messages: `openStack` (stack load), `openCard`/
  `closeCard` (navigation, including HyperTalk-initiated `go`), `mouseDown`,
  `mouseUp`, `mouseStillDown` optional-later. `HandleCardClick` dispatches
  `mouseDown` on press and `mouseUp` on release over the same part
  (HyperCard fires mouseUp only if release is inside the pressed part).
  Set `me` and `the target` in the execution environment. Remove the
  empty-script early-return.
- **Accept:** Fixture stack where the *background* script implements
  `on mouseUp go next card` and buttons have empty scripts: clicking any
  button navigates. `openCard` handler that puts text into a field runs on
  navigation. Unit tests for pass/fall-through ordering.

### C2. AutoHilite and visual click feedback  [model: sonnet] [status: pending]

- **Spec:** On mouseDown over a button with autoHilite: render hilited
  (invert per B2), un-hilite on release (checkBox/radioButton toggle their
  hilite property instead). Property `hilite` readable/writable from
  HyperTalk (`set the hilite of button X to true`).
- **Accept:** Golden pair (normal/hilited) plus interpreter test for
  get/set hilite.

### C3. Cursor and hit-test parity  [model: haiku] [status: pending]

- **Spec:** Hit-testing must ignore invisible parts (already does), respect
  z-order top-down (card parts before background parts, last-listed first
  within a layer; verify current loop order matches and fix if not), and
  exclude fields unless locked (`lockText` fields receive mouseUp; unlocked
  fields would enter edit mode, which is out of scope, so treat unlocked
  fields as transparent to clicks for now and log). Browse-hand cursor over
  the card area.
- **Accept:** Unit tests for overlap ordering and field lockText behavior.

### C4. Scrolling fields, find, and remaining Phase-10 language items  [model: opus] [status: pending]

- **Spec:** Functional scrollbars on scrolling fields (wheel + arrow boxes);
  `find` command across card/background fields; chunk expressions
  (`char/word/item/line x of y`) completed in the interpreter with tests
  from real stack scripts collected in `docs/hypertalk-coverage.md`.
- **Accept:** Coverage doc rows flip to done with linked tests.

---

## 7. Phase D: Resource-fork features (requires A5)

### D1. Resource fork parser  [model: sonnet] [status: pending]

- **Files:** new `Core/Resources/ResourceForkParser.cs` (+tests).
- **Spec:** Classic resource map: 16-byte header (data offset, map offset,
  lengths), type list, reference lists, name list; expose
  `IReadOnlyList<ResourceEntry> { Type(4cc), Id(short), Name, Data }`.
  Big-endian throughout; attribute byte masked off the 3-byte data offset.
- **Accept:** Parses the Cyberdelia resource fork (610 bytes) and lists its
  types; round-trip unit test on a synthetic fork.

### D2. ICON and cicn decoding + button icons  [model: sonnet] [status: pending]

- **Spec:** `ICON` = 128 bytes, 32x32 1-bit. `cicn` = color icon with IconData
  header, mask + 1-bit + color planes (implement B&W path first: mask +
  bitmap). Wire into B2's icon slot: search stack resource fork by id; on
  miss consult the built-in-ID replacement table (Asset Policy 4); on miss
  draw placeholder and log once per id.
- **Accept:** A stack with embedded ICONs shows them; missing ids log
  gracefully.

### D3. snd resource playback  [model: opus] [status: pending]

- **Spec:** Parse `snd ` format 1/2, extract sampled sound (squareWaveSynth/
  sampledSynth cmd 81 bufferCmd), 8-bit unsigned mono at 11/22 kHz typical;
  resample and play via a small cross-platform audio out (evaluate: LibVLC
  already planned for MOV; `System.Media` is Windows-only; consider
  OpenAL/miniaudio binding or feed PCM to LibVLC). `play "name"` HyperTalk
  command wires to it; `beep` maps to a bundled original beep sample
  (generate a sine, do not copy Apple's).
- **Accept:** `play` in a fixture script produces audio on all three OSes;
  unsupported formats log and continue.

### D4. AddColor (HCcd/HCbg) rendering  [model: fable] [status: pending]

- **Spec:** Decode `HCcd`/`HCbg` resources (format doc:
  hypercard.org/addcolor_resource_format/): per-element records coloring
  rects/buttons/fields/PICTs with RGB and blend. Render as an overlay pass
  in `RenderMode.Color`: colorized elements multiply/replace per record
  order onto the 1-bit base. Un-gray the View menu item (B6). PICT elements
  may depend on D6; render what is decodable, log the rest.
- **Accept:** A known AddColor stack (source one via archive.org, add to
  docs/samples.md) renders colored; B&W mode remains bit-identical to before.

### D5. StuffIt LZAH (method 5) decompressor  [model: fable] [status: pending]

- **Spec:** LZAH is LZSS with adaptive Huffman coding (LHarc lh1 family).
  Clean-room from format descriptions (The Unarchiver wiki, LHA
  documentation); do not port GPL/LGPL sources verbatim. Window 4KB,
  match lengths 3-60, adaptive frequency tree with rebalancing at cap.
  Wire as `MethodLzah = 5` in `StuffItExtractor`.
- **Accept:** `BeavisEmulatorV2.sit` extracts; its data fork (43,040 bytes)
  has STAK magic; CRCs in the entry header verify (implement the SIT CRC-16
  check while here and validate all methods against it).

### D6. StuffIt 5 extractor  [model: fable] [status: pending]

- **Spec:** New `StuffIt5Extractor` for the `StuffIt (c)1997-` container:
  80-byte archive header, entry headers with variable-length metadata,
  methods 0/13/15 (Arsenic = BWT + MTF + RLE + arithmetic coding; largest
  sub-task, may be split: implement 0 and 13 first, log 15). Register ahead
  of the classic extractor in `ContainerPipeline`.
- **Accept:** `ContextualMenus.sit` extracts and its stack renders; method 15
  archives at minimum enumerate their file table and report the unsupported
  method per file.

### D7. Embedded font resources (FOND/NFNT)  [model: fable] [status: pending]

- **Spec:** When the user's stack carries FOND/NFNT bitmap fonts, decode
  NFNT strike (bit image, location table, offset/width table) and render
  field text with the *actual* embedded font at native size, bypassing
  FontMapper. This is the maximum-fidelity path and is fully within the
  Asset Policy (user-supplied data). Structure it as an
  `IGlyphSource` abstraction over (bundled TTF | embedded NFNT) so
  TextRenderer stays single-purpose.
- **Accept:** A fixture stack with an embedded NFNT renders using it
  (compare against emulator screenshot); stacks without embedded fonts are
  unaffected.

---

## 8. Phase E: System 7 shell chrome (after B, C)

The user-facing shell should read as a Macintosh regardless of host OS. This
work resumes only once Phases A-C are done; card fidelity outranks chrome.

### E1. Stabilize and modularize the chrome  [model: sonnet] [status: pending]

- **Spec:** Move `System7MenuBar`, `System7TitleBar`, and window styling into
  `App/Chrome/` with a `README.md` documenting the S7 metrics used (menu bar
  height 20px equivalent, title bar rake pattern, 3D border palette from the
  AGENTS.md styling conventions). Kill the remaining platform landmines
  documented in commit history (transparency + CornerRadius on Windows) with
  explicit per-OS notes and a smoke test that the window materializes on
  each OS in CI (Avalonia headless test package).
- **Accept:** Headless UI test instantiates MainWindow on the CI matrix; the
  chrome renders from one code path with no OS-conditional visual
  differences beyond what is documented.

### E2. In-window HyperCard furniture  [model: sonnet] [status: pending]

- **Spec:** The card area gets period-correct furniture driven by stack
  state, not hardcoded: the message box (`msg`) window (HyperTalk `put`
  without target writes here), Go menu (Back, Home, Recent, First/Prev/
  Next/Last mapping to existing navigation), visual-effect transitions on
  `go` (dissolve, wipe l/r/u/d, iris open/close, checkerboard: implement as
  SkiaSharp frame sequences between old and new card snapshots, ~15 frames,
  timed per `fast/slow` qualifiers).
- **Accept:** `visual effect dissolve` followed by `go next card` animates;
  Go menu items work; message box accepts and evaluates one-line HyperTalk.

### E3. About/help polish and stack-picker consistency  [model: haiku] [status: pending]

- **Spec:** Sweep all dialogs against the System 7 Dialog Styling
  Conventions table in AGENTS.md; fix drift; ensure every dialog is
  keyboard-complete (Return = default, Esc = cancel).
- **Accept:** Visual checklist in PR description with screenshots per dialog.

---

## 9. Ongoing: research and stretch

- **R1. HC 1.x format divergences** [fable]: PAGE/LIST width differences,
  part layout deltas; acquire 1.x samples, extend parsers behind a version
  switch read from the STAK block (`FormatVersion`). Document in
  stack-format.md.
- **R2. PICT decoder (subset)** [fable]: QuickDraw opcode replay for the
  opcodes that actually occur in stack resources (start: clipping, bitmaps
  via PackBits, rects/lines/text). Log unknown opcodes with offsets. Needed
  fully only for AddColor PICT elements and card `PICT` resources.
- **R3. HyperCard 2.4 password / private access** [fable]: research only;
  never bypass protection on stacks the user does not own; document findings.
- **R4. Built-in icon replacement set** [human + haiku]: commission or draw
  original 32x32 pixel art for the ~40 most-referenced built-in icon ids;
  wire into D2's table. Requires human judgment on art and licensing.
- **R5. QuickTime MOV via LibVLC** [sonnet]: the original PLAN.md Phase 9;
  unblocked after D1 (movies may be referenced by file path or resource).

---

## 10. Standing verification checklist (every task)

1. `dotnet build` zero errors; do not introduce new warnings beyond the
   known `RawStackScanner.cs(109)` CS8600.
2. `dotnet test` green; new behavior has a test or a golden image.
3. `tools/RenderDump` against all six samples: no crashes, log output clean.
4. Net-diff review per the Regression Check Policy in AGENTS.md.
5. Update `docs/stack-format.md` / `docs/hypertalk-coverage.md` when format
   or language knowledge changed.
6. Commit per Commit Policy (imperative message, Copilot co-author trailer).
