# HyperCard# Roadmap

This document is the durable, session-surviving plan for HyperCard#. It
records what has been diagnosed and verified, what remains to build, in what
order, and which class of AI model (or human) each task suits. It supersedes
`docs/PLAN.md` (kept for history).

Last full verification pass: 2026-07-11, at the merge of the fidelity
diagnosis branch with the Phase 9 work (`0669b6b`). Every finding below was
re-tested against that HEAD by building the solution and rendering the sample
stacks headless to PNG. Line numbers drift; re-verify before fixing.

**How to use this document.** Each task has an ID (A1, B2...), a goal, the
files involved, an explicit spec, acceptance criteria, and a model
recommendation. When a task is finished, change its status line, note the
commit hash, and keep the spec text so later readers understand what was done
and why. Never mark a phase complete while any task in it is `pending` (see
the Phase Completion Guardrail in `AGENTS.md`). Start every session with
`git pull` and re-verify a finding's status before working it: the original
diagnosis was made on a stale clone that was several large commits behind
origin, and half its findings were already fixed upstream.

**Model routing guide.**

| Model | Use for | Rule of thumb |
|-------|---------|---------------|
| haiku | Mechanical, fully-specified, single-file edits with a clear pass/fail test | The spec contains every constant needed; no design decisions remain |
| sonnet | Multi-file feature work following an explicit spec or a named reference implementation | Design is settled; the task is careful implementation and testing |
| opus | Cross-cutting design, porting from foreign-language references, debugging without a known cause | Judgment calls remain; the spec defines goal and constraints, not shape |
| fable | Format reverse engineering with sparse references, architectural rewrites, anything that may invalidate assumptions across modules | Highest ambiguity and blast radius |

A larger model can always take a smaller model's task. The reverse is the
risk.

---

## 1. Findings ledger (verified 2026-07-11)

Diagnosis method: headless render harness (see task A0) run against all six
sample files, plus targeted source inspection. Statuses: `open`, `partial`,
`fixed`.

- **F1 [partial] Part chrome rendering.** Originally buttons/fields were
  never drawn (the old `PartRenderer` assumed chrome was baked into the WOBA
  bitmap, which is false; HyperCard drew all part chrome live). As of
  `0669b6b`, `PartRenderer` draws field fills/borders/shadows, scrollbar
  chrome, button chrome with icons from the stack's resource fork, and
  styled text. Verified visually: NEUROBLAST card 1 renders its "Go Back"
  roundRect button with icon and its opaque field correctly. Remaining work:
  audit every `PartStyle` value, hilite/autoHilite states, checkBox/
  radioButton glyphs, popup style, `showName`/`ShowLines`/`WideMargins`
  flags, and lock it all in with golden tests (tasks B2, B7).
- **F2 [fixed] Message-passing hierarchy.** `StackViewModel.HandleCardClick`
  now walks part -> card -> background -> stack with Handled/Passed
  semantics, and `openCard` lifecycle messages fire on navigation. Verify
  `openStack`/`closeCard`/`mouseDown` coverage during C1 before closing that
  task.
- **F3 [fixed] .sit extraction encoding failure.** The old code called
  `Encoding.GetEncoding("macintosh")`, which always throws on .NET 8 without
  a registered `CodePagesEncodingProvider`; a blanket catch swallowed it, so
  every StuffIt extraction silently returned nothing. Fixed by the custom
  table in `Core/Text/MacRomanEncoding.cs` (no external package, better).
  Verified: `NEUROBLAST_Cyberdelia.sit` extracts by name and renders.
- **F4 [worked around, not root-cause fixed] Wrong HFS signature constant.**
  `HfsReader.cs` and `HfsExtractor.cs` still define
  `HfsMdbSignature = 0xD2D7`. That is the MFS (400K floppy) signature;
  classic HFS is `0x4244` (ASCII "BD"), confirmed present at offset 1024 in
  both sample .img files. The primary signature check therefore still
  fails on real HFS volumes. Both .img samples open correctly today only
  because `HfsReader.IsHfs()` (verified 2026-07-11 via the new render
  harness) falls back to a heuristic: if the MDB+2 field looks like a
  plausible Mac timestamp, it accepts the volume regardless of the
  signature word. That masks the bug rather than fixing it, and risks a
  false positive on non-HFS data with a coincidentally plausible
  timestamp. Task A2.
- **F5 [fixed, B1] WOBA mask layer discarded.** Was: `WobaDecoder.cs` decoded
  the mask then output only the image layer; `CardRenderer` drew the card
  layer opaque in B&W mode regardless. Fixed by carrying a real `Mask` array
  through `BitmapImage` and compositing all three WOBA pixel states
  (opaque black / opaque white / transparent) uniformly in both render
  modes. The sample corpus never exercised this (its art is card-level,
  self-masked, full-bleed) — proven instead by two new hand-built fixture
  tests. See B1 for the full account, including a partial prior compensation
  (a Color-mode-only, heuristic-based transparency path) that this
  superseded.
- **F6 [fixed] Resource forks discarded.** `Containers/StackEntry.cs` now
  carries `ResourceFork`, `Resources/MacResourceForkReader.cs` parses it,
  and button icons already consume it. Fork-dependent features (sound,
  AddColor, embedded fonts) build on this (Phase D).
- **F7 [fixed] Latin1 instead of MacRoman.** No `Encoding.Latin1` remains in
  `src/` text decoding; all call sites use `MacRomanEncoding`.
- **F8 [fixed] Styled text runs unused.** `TextRenderer.DrawStyledText`
  resolves `StyleRun`s against `StyleTableBlock`/`FontTableBlock`. Golden
  coverage still needed (B7).
- **F9 [partial] Font fidelity.** Multi-tier `FontMapper` (user `fonts/`
  directory, system fonts, embedded ChicagoFLF + Noto Sans, generic
  fallbacks) is implemented per the Font Policy in `AGENTS.md`. Remaining:
  verify text draws with antialiasing off and integer-scale
  nearest-neighbor zoom so pixels stay crisp (task B3).
- **F10 [crash fixed by A4; underlying formats still unsupported] Unsupported
  container variants degrade, no longer crash.**
  (a) `BeavisEmulatorV2.sit` uses StuffIt compression method 5, which is
  LZAH (LZSS *plus adaptive Huffman*). The current `DecompressLzss` is plain
  LZSS, so it still emits 43,040 bytes of garbage — but as of A4,
  `StackParser.Parse` now rejects that garbage with a typed
  `StackFormatException` instead of crashing with
  `ArgumentOutOfRangeException`. Actually decoding this archive still needs
  the Huffman stage (D5). (b) `ContextualMenus.sit` is StuffIt 5 format
  (`StuffIt (c)1997-` magic); `StuffItExtractor.IsStuffIt5` already
  detected it, and A4 made `ContainerPipeline` report that fact with a
  specific message instead of a generic one. Actually extracting it still
  needs a SIT5 implementation (D6).

**Commit-history lesson.** Earlier development spent ~15 consecutive commits
on window chrome while the card canvas had the defects above. Chrome
fidelity stays a goal (Phase E), sequenced after card fidelity: the
preservation mission lives inside the 512x342 rectangle.

---

## 2. Asset and legal policy

Researched 2026-07-11. Classic Mac OS and HyperCard are **not** public
domain and were never freeware in a legally durable sense. Apple made some
old system software freely downloadable years ago but retained copyright;
"abandonware" is a community label with no legal force.

Policy (aligned with the Font Policy already in `AGENTS.md`):

1. **Bundle nothing extracted from Apple software.** No NFNT bitmaps ripped
   from a System file, no HyperCard built-in icon bitmaps, no Apple `snd `
   or PICT resources.
2. **Render everything the user's own files contain.** Stacks and disk
   images supplied by the user often embed the fonts, icons, sounds, and
   color resources they need. Decoding user-supplied data is the product's
   core function and mirrors how emulator-based preservation works. This is
   already the pattern for button icons and the user `fonts/` directory;
   extend it to sounds, PICTs, AddColor, and embedded NFNT fonts (Phase D).
3. **Open recreations for defaults.** Bundled: ChicagoFLF (public domain),
   Noto Sans (SIL OFL). Candidates to evaluate (verify license text before
   bundling): Urban Renewal set (Kreative Korp), ChiKareGo2.
4. **Built-in icon IDs.** Stacks reference HyperCard's built-in icons by ID;
   those bitmaps live in HyperCard itself and must not be copied. Maintain a
   table of well-known IDs mapped to original replacement pixel art drawn
   from scratch for this repo (task R4). Until then: neutral 32x32
   placeholder frame plus one log line per missing ID.

---

## 3. Modularity and documentation charter

Goal: any single task in this roadmap can be executed by a small model or a
new human contributor without holding the whole system in their head.

**Module boundaries (enforced by project references, no cycles):**

- `Core` knows bytes and models; never references SkiaSharp, Avalonia, or
  the interpreter.
- `Rendering` knows Core and SkiaSharp; never references Avalonia.
- `HyperTalk` knows Core (object resolution) and nothing visual.
- `App` composes the other three; all Avalonia types stay here.
- `Core/Resources/` owns resource-fork decoding (icons, sounds, color,
  fonts). `App/Chrome/` (to be created, task E1) owns all System 7
  look-alike window furniture.

**Code rules:**

- One binary block type, one file; one decoder, one file.
- Byte-offset knowledge lives inline next to the reads (the header-comment
  style of `CardBlock.cs` is the model).
- Public APIs carry XML doc comments stating units, coordinate systems
  (HyperCard rects are top,left,bottom,right in card coordinates), and
  endianness assumptions.
- Files stay under ~400 lines; split by responsibility past that.
  (Current debt: `HyperTalkInterpreter.cs` is ~1,600 lines after Phase 9;
  splitting it into statement/expression/property partials is task M1.)
- Every `src/` project gets a one-screen `README.md`: what it owns, what it
  must not reference, where its tests live (task M2).
- No emoji, no em-dashes in code or docs (house style).

**Maintenance tasks:**

- **M1. Split the interpreter** [sonnet] [status: pending]: partial classes
  or focused files (statements, expressions, built-in functions,
  properties, chunk expressions) with no behavior change; tests must pass
  unchanged. Do after C1 to avoid churn.
- **M2. Per-project READMEs + AGENTS.md file-map refresh** [haiku]
  [status: pending]: after each phase completes, regenerate the Quick File
  Reference table in `AGENTS.md` and each project README.
- **M3. Create missing research docs** [haiku] [status: pending]:
  `docs/stack-format.md` (block map; CARD/BKGD/part layouts from code
  comments; WOBA semantics; container formats incl. SIT compression method
  table: 0 none, 1 RLE90, 2 LZW, 3 Huffman, 5 LZAH, 13 LZSS+Huffman,
  15 Arsenic; HFS MDB `0x4244` at offset 1024 vs MFS `0xD2D7`) and
  `docs/hypertalk-coverage.md` (statement/function/property table with
  status, generated by reading `HyperTalkInterpreter.cs` and
  `AstNodes.cs`). Both are already referenced by `AGENTS.md` but do not
  exist.

---

## 4. Phase A: Every sample opens, nothing crashes

**Retired task IDs:** A1 (MacRoman encoding — already fixed upstream, verified by A0 render harness), A3 (StuffIt verification — already works via upstream A1 fix), A5 (resource-fork carrying — already implemented upstream). Task numbering is never reused to keep IDs stable across sessions; gaps are documented here.

### A0. Commit the render-dump harness as a tool  [model: sonnet] [status: done, commit TBD]

- **Goal:** A repeatable CLI rendering any stack's cards to PNGs, so
  rendering tasks show before/after evidence and CI can diff golden images.
- **Files:** `tools/RenderDump/RenderDump.csproj`, `Program.cs`, `README.md`;
  added to `HyperCardSharp.sln` under a `tools` solution folder. Also fixed
  `tests/HyperCardSharp.Core.Tests/HyperCardSharp.Core.Tests.csproj`, which
  was missing the same Linux native-asset package and made
  `Phase24CorpusTests.AllSamples_FirstCardRendersWithoutException` fail on
  this dev machine with a `SKObject` type-initializer exception (not a
  product bug; confirmed identical failure on a clean stash before this
  task touched anything).
- **Spec (as built):** Console app referencing Core + Rendering. Args:
  `<input-file> <out-dir> [max-cards]` renders; `--info <input-file>
  [max-cards]` prints the block inventory (`StackParser.GetBlockInventory`)
  and a per-card part table without rendering. Uses
  `ContainerPipeline.UnwrapEntries` (the fork-carrying API, not the older
  `UnwrapMultiple` tuple form) so resource-fork size is visible in the log.
  All pipeline log lines are echoed. `SkiaSharp.NativeAssets.Linux.NoDependencies`
  pinned to the same version as `SkiaSharp` (3.119.2).
- **Verified:** Ran against all six samples. Five render cleanly
  (`NEUROBLAST_HyperCard`, `.img`, `NEUROBLAST_Cyberdelia.sit`, `.img`,
  and — confirming F10(a) is still open — `BeavisEmulatorV2.sit` fails
  its `StackParser.Parse` call with the expected `ArgumentOutOfRangeException`,
  caught and reported by the tool rather than crashing it).
  `ContextualMenus.sit` correctly reports zero stacks found (F10(b), SIT5
  still unsupported). `dotnet build` and `dotnet test`: 0 errors, 0
  warnings, 39/39 tests green.
- **Finding surfaced by this task (updates F4 below):** `HfsExtractor`
  logged `"HFS volume found with 1 stack(s)"` for both `.img` samples,
  which looked like F4 was already fixed. It is not: `HfsMdbSignature` is
  still the wrong constant (`0xD2D7`, the MFS signature) in both
  `HfsReader.cs` and `HfsExtractor.cs`. What actually makes it work is a
  heuristic added to `HfsReader.IsHfs()` that accepts any volume whose
  MDB+2 field looks like a plausible Mac timestamp, bypassing the
  signature check entirely when it fails. This is a symptom patch, not
  the root-cause fix `AGENTS.md`'s Root-Cause Policy calls for: the
  constant is still mislabeled, and the heuristic could accept
  non-HFS data that happens to have a plausible-looking timestamp field.
  A2 is revised below to fix the constant itself and tighten the
  heuristic to a documented fallback rather than the primary path.

### A2. Fix the HFS signature constant; demote the timestamp heuristic to a fallback  [model: haiku] [status: done, commit 3c2ba0f]

- **Goal:** Close F4 for real. `.img` samples already extract via HFS today
  (A0 confirmed this), but only because a heuristic masks a wrong constant.
  This task fixes the constant so the primary, well-defined check is the
  one that actually matches real HFS volumes, and keeps the heuristic only
  as a documented last resort for non-standard imaging tools.
- **Files:** `Core/Containers/HfsReader.cs`, `HfsExtractor.cs`.
- **Spec:** Change `HfsMdbSignature` from `0xD2D7` to `0x4244` in both
  files (add a one-line comment noting `0xD2D7` is the *MFS* signature, to
  stop the next reader making the same mistake). Leave `IsHfs()`'s
  timestamp-plausibility fallback in place for the non-standard-tool case
  it was added for, but reorder/comment it so it reads as an explicit
  fallback path after the corrected primary check, not as the thing
  silently doing all the work.
- **Accept:** `HfsReader.IsHfs()` returns true for both sample .img files
  via the primary signature check alone (add a unit test asserting this
  directly, bypassing the heuristic, e.g. by truncating the MDB+2 bytes to
  an implausible value and confirming the signature check alone still
  passes). Render harness output for both samples is unchanged. Existing
  `dotnet test` suite (39 tests) stays green.

### A4. Never crash on unrecognized or corrupt input  [model: sonnet] [status: done, commit ae7818f]

- **Goal:** Close the F10 crash path; enforce the graceful-degradation UX
  rule ("never a crash").
- **Files (as built):** `Core/Stack/StackParser.cs`,
  `Core/Stack/StackFormatException.cs` (new),
  `Core/Containers/ContainerPipeline.cs`,
  `Core/Containers/StuffItExtractor.cs`. `Core/Binary/MagicDetector.cs`
  and `App/ViewModels/StackViewModel.cs` were **not** touched: `MagicDetector`
  turned out to be dead code (zero call sites outside its own file/test —
  detection actually happens per-extractor via `CanHandle()` inside
  `ContainerPipeline`), and `StackViewModel.LoadStack`/`MainWindow.OpenFileAsync`
  already forward exception/log messages verbatim into `StatusText`, so
  fixing the message at the source was sufficient without App changes.
- **Spec (as built):** `EnumerateBlocks` rejects headers with `Size < 16`
  or `FileOffset + Size > data.Length`, logging the specific reason and
  stopping enumeration (same graceful-stop pattern as the pre-existing
  `size <= 0` check) — this is the actual fix, since it prevents the bad
  header from ever reaching `Parse()`'s slicing. `StackFormatException`
  (carries a byte offset) replaces `InvalidDataException`/raw BCL
  exceptions; `Parse()` wraps both the per-block dispatch loop and the
  PAGE-parsing loop so inner-content failures also get offset context.
  `StuffItExtractor.IsStuffIt5` (already existed privately, detecting the
  `"StuffIt "` banner) promoted to public; `ContainerPipeline.Unwrap` and
  `UnwrapMultiple` both check it to log "StuffIt 5.x/Aladdin archives are
  not yet supported..." instead of the generic "found no STAK entries."
- **Verified:** `dotnet test` 50/50 (8 new: 4 StackParser bounds/exception
  tests incl. a random-garbage fuzz case, 4 SIT5-message tests). RenderDump
  swept all six samples: the four healthy ones render identically to
  before A4 (unchanged card counts/IDs); `BeavisEmulatorV2.sit` now fails
  with `StackFormatException: No STAK block found in file (at offset 0x0)`
  instead of a raw `ArgumentOutOfRangeException`; `ContextualMenus.sit`
  reports the specific SIT5-unsupported message instead of a generic one.
  Random 256-byte garbage also fails gracefully with the same typed
  exception, confirming the fix generalizes beyond the one sample that
  exposed it. (A third occurrence of A2's `0xD2D7` bug was noticed while
  reading nearby code during this task — see the Phase A exit-criteria
  note below; left unfixed here as out of A4's scope.)

**Phase A exit criteria: met, 2026-07-11.** All six samples either open
with correct names via their intended container path (4/6) or fail with a
specific, readable message (`BeavisEmulatorV2.sit`, `ContextualMenus.sit`
— both still unsupported *formats*, per D5/D6, but no longer crash and no
longer show a misleading message). `dotnet test`: 50/50 green.

**Known follow-up left for a later session (not blocking Phase A):**
`ContainerPipeline.TryExtractHfsResourceForks` has its own inline
`hfsSig = 0xD2D7` constant — a third occurrence of the bug A2 fixed in
`HfsReader.cs`/`HfsExtractor.cs`, masked by the same timestamp heuristic
inline in that method so it isn't currently user-visible. One-line fix,
same pattern as A2's commit (`3c2ba0f`).

---

## 5. Phase B: Card rendering is provably faithful

Reference implementation throughout: HyperCardPreview (Swift, GPL,
github.com/PierreLorenzi/HyperCardPreview), especially part drawing and
text layout. Use it as a *specification* (read, understand, re-express);
never translate GPL code line by line into this MIT project.

### B1. Carry the WOBA mask through to compositing  [model: sonnet] [status: done, commit 9591d86]

- **Goal:** Close F5: correct compositing (background paint -> background
  parts -> card paint -> card parts) with true card-layer transparency.
- **Files (as built):** same four files as planned, plus
  `tests/HyperCardSharp.Rendering.Tests/HyperCardSharp.Rendering.Tests.csproj`
  (needed the same `SkiaSharp.NativeAssets.Linux.NoDependencies` package
  A0 added elsewhere, to make `SKBitmap.GetPixel` work in tests on Linux).
- **Found before implementing (revises the original F5 diagnosis):** a prior
  session had already added a second `BitmapRenderer` conversion method and
  wired `isColor`-gated transparency into `CardRenderer` — but it only
  activated in Color mode (never in B&W, the default/primary mode), and its
  "transparency" was a naive "any white pixel is transparent" heuristic
  applied post-hoc to the SKBitmap conversion, not derived from the real
  WOBA mask (`WobaDecoder` still discarded the decoded mask array
  unconditionally). So F5's user-visible symptom was unchanged; the fix
  below replaces this heuristic rather than building alongside it.
- **Spec (as built):** `BitmapImage` gained `byte[] Mask`. WOBA semantics
  (HyperCardPreview + the Wildfire format doc): pixel is *black* when Data
  bit = 1; *opaque white* when Mask bit = 1 and Data bit = 0; *transparent*
  when both are 0. Self-masking (`MaskDataSize == 0`, empty `MaskRect`)
  sets `Mask = Data` (aliased, not copied — decoded arrays are never
  mutated post-decode elsewhere in the codebase, so this is safe and
  avoids an allocation on the common case). `BitmapRenderer`'s two
  conversion methods consolidated into one three-state `ToSKBitmap`, with
  a documented fallback to fully-opaque-white when `Mask` is absent/wrong
  length (covers hand-built fixtures). `CardRenderer` draws both bg and
  card bitmap layers through the single method unconditionally (not
  gated on Color mode — a transparent pixel over the white canvas looks
  identical to opaque white, so this is correct in B&W mode too, and
  improves Color-mode AddColor fidelity as a side effect). Dropped the
  now-redundant `_bitmapCacheAlpha`. `CardBlock.HideCardPicture` /
  `BackgroundBlock.HideBackgroundPicture` (existed, were never consulted
  anywhere) are now wired in.
- **Verified:** 6 new tests (2 `WobaDecoderTests` against a hand-built
  16x16 three-state BMAP fixture with real separate mask data, verifying
  `Mask` is distinct from `Data` and each of the three states decodes
  correctly, plus a self-masking case verifying `Mask == Data`; 4
  `BitmapRendererTests` asserting the three composited SKBitmap pixel
  states directly via `GetPixel`, including the no-mask-provided fallback).
  Suite: 58/58 green (was 52). RenderDump swept all six samples: output
  byte-identical to pre-B1 (the sample corpus is self-masked/full-bleed,
  so it never exercised the separate-mask path — the fix is proven by the
  fixture tests, not the samples, exactly as anticipated in the original
  accept criteria below).
- **Accept (met):** Unit tests on a hand-built BMAP fixture with known
  mask/image bits assert all three pixel states in the composited output.
  NEUROBLAST renders unchanged (self-contained cards). A fixture stack
  whose background carries art would no longer be whited out (no sample
  in the corpus exercises this scenario; covered by the fixture tests
  instead — worth sourcing such a sample for the B7 golden-image suite).

### B2. Part-style audit and completion  [model: sonnet] [status: pending]

- **Goal:** Take F1 from partial to done: every `PartStyle` renders
  correctly in both normal and hilited states.
- **Files:** `Rendering/PartRenderer.cs`, `Core/Parts/PartStyle.cs`,
  `docs/stack-format.md`.
- **Spec:** Verify the style enum against the format doc (0 transparent,
  1 opaque, 2 rectangle, 3 roundRect, 4 shadow, 5 checkBox, 6 radioButton,
  7 scrolling, 8 standard, 9 default, 10 oval, 11 popup) and audit the
  existing chrome for each: checkBox (12x12 box, x/check when hilited),
  radioButton (12x12 circle, filled dot when hilited), default (extra 3px
  rounded outer ring), oval (ellipse hit region and frame), popup (frame,
  title-width label region, drop-down arrow). Hilite = invert the part
  rect (SkiaSharp `SKBlendMode.Difference` white fill or layer inversion).
  Verify flag semantics with fixture-byte unit tests and record them in
  `docs/stack-format.md`: hidden bit, hilite bit, autoHilite, showName,
  sharedHilite, showLines, wideMargins, lockText.
- **Accept:** A synthetic part-showcase fixture stack (built by a test
  utility writing CARD/BKGD bytes, or a freely-shareable stack documented
  in `docs/samples.md`) renders every style; each style has a golden image
  in both states.

### B3. Pixel-crisp text and integer zoom  [model: haiku] [status: pending]

- **Goal:** Close the remainder of F9: authentic 1-bit text rendering.
- **Files:** `Rendering/TextRenderer.cs`, `App/Controls/SkiaBitmapControl.cs`.
- **Spec:** All `SKFont` instances: `Edging = SKFontEdging.Alias`,
  `Subpixel = false`, `Hinting = SKFontHinting.None`. The display control
  scales the 1x card bitmap with
  `SKSamplingOptions(SKFilterMode.Nearest)` at integer zoom factors.
  Add a B&W purity test: in `RenderMode.BlackAndWhite`, every rendered
  pixel is pure `#000000` or `#FFFFFF` (this also guards future work).
- **Accept:** Purity test green; a 2x zoom screenshot shows blocky
  doubled pixels, not smoothed edges.

### B7. Golden-image regression suite  [model: sonnet] [status: pending]

- **Goal:** Lock fidelity so later work cannot silently regress it
  (styled text F8 and font tiers F9 currently have no pixel-level tests).
- **Files:** `tests/HyperCardSharp.Rendering.Tests/Golden/`, CI workflow.
- **Spec:** Helper renders named fixture cards and byte-compares PNGs to
  committed goldens (tolerance 0 in B&W mode). `UPDATE_GOLDENS=1`
  regenerates. Fixtures: the B2 part showcase, a styled-text card, one
  NEUROBLAST card and one Cyberdelia card (samples are gitignored; gate on
  file presence locally, download via `docs/samples.md` script in CI).
  Font-dependent goldens must render from the *embedded* fonts only, with
  the user/system tiers disabled in test config, so results are
  machine-independent.
- **Accept:** CI fails when a rendering change alters goldens without
  regeneration.

**Phase B exit criteria:** A stack with standard buttons and fields is
visually indistinguishable from a period screenshot at 1x except for
resources the file does not contain; golden suite green in CI.

---

## 6. Phase C: Faithful behavior

### C1. Complete the message model  [model: sonnet] [status: pending]

- **Goal:** Finish F2's remainder. The part -> card -> background -> stack
  walk and `openCard` exist; complete the system-message set and target
  context.
- **Files:** `HyperTalk/MessagePassing/MessageDispatcher.cs`,
  `App/ViewModels/StackViewModel.cs`, interpreter environment.
- **Spec:** Fire `openStack` on load, `closeCard` before navigation,
  `mouseDown` on press (mouseUp only when release lands in the same part,
  as HyperCard does). Ensure `pass mouseUp` in a part script continues the
  climb (add tests for Handled vs Passed vs NotFound at each level). Set
  `me` and `the target` for every dispatch. Document the message order in
  `docs/hypertalk-coverage.md`.
- **Accept:** Fixture stack tests: background-script navigation with
  empty-scripted buttons works; `openCard` populates a field on arrival;
  `pass` chains verified by unit tests.

### C2. AutoHilite and click feedback  [model: sonnet] [status: pending]

- **Spec:** mouseDown over an autoHilite button renders it hilited until
  release; checkBox/radioButton toggle their persistent `hilite` property
  instead. `hilite` readable and writable from HyperTalk
  (`set the hilite of button X to true`). Radio buttons in the same family
  (low nibble of the family field) unset siblings.
- **Accept:** Golden pair (normal/hilited) for standard, checkBox,
  radioButton; interpreter get/set tests; family-exclusivity test.

### C3. Hit-test parity  [model: haiku] [status: pending]

- **Spec:** Hit-testing top-down: card parts before background parts,
  last-listed first within a layer (verify the current loop order; fix if
  reversed). Unlocked fields swallow clicks without dispatching button
  messages (edit mode is out of scope; log once); `lockText` fields
  dispatch `mouseUp`. Browse-hand cursor over the card area.
- **Accept:** Unit tests for overlap ordering and lockText routing.

### C4. Remaining language surface  [model: opus] [status: pending]

- **Spec:** Work from `docs/hypertalk-coverage.md` (M3): complete chunk
  expressions (`char/word/item/line x of y` as containers and sources),
  `find`, `sort`, date/time functions, and the properties real sample
  scripts use (collect misses by running the samples and logging
  unhandled constructs). Keep the AST explicit; no string re-parsing at
  execution time.
- **Accept:** Coverage doc rows flip to done with linked tests; sample
  stacks' scripts execute without "can't understand" noise in the log.

---

## 7. Phase D: Resource-fork media (F6 groundwork is done)

### D2. Icon completeness  [model: haiku] [status: pending]

- **Spec:** Button icons from stack `ICON` resources work; add `cicn`
  (B&W path: mask + bitmap planes) and the built-in-ID fallback table per
  Asset Policy 4 (placeholder + one log line per missing ID until R4
  supplies art).
- **Accept:** Stack with `cicn` icons renders; missing built-in IDs log
  once each, render placeholder, never crash.

### D3. Sound playback  [model: opus] [status: pending]

- **Spec:** `Resources/SoundDecoder.cs` exists (verify format 1/2
  coverage: sampledSynth, bufferCmd 0x8051, 8-bit unsigned mono, typical
  11/22 kHz). Missing piece is *output*: pick a cross-platform audio path
  (evaluate feeding PCM to LibVLC, already a planned dependency, vs an
  OpenAL/miniaudio binding; `System.Media` is Windows-only). Wire
  HyperTalk `play "name"` and `beep` (generated sine, never Apple's
  sample).
- **Accept:** `play` in a fixture script produces audio on Windows,
  macOS, Linux; unsupported snd formats log and continue.

### D4. AddColor rendering  [model: fable] [status: pending]

- **Spec:** `Resources/AddColorDecoder.cs` has a start; finish per
  hypercard.org/addcolor_resource_format/: `HCcd`/`HCbg` element records
  (rect/button/field/PICT/resource elements with RGB and blend flags),
  applied in record order as a color overlay pass in `RenderMode.Color`.
  PICT elements depend on the `PictDecoder`; render what decodes, log the
  rest. B&W mode must remain bit-identical (guarded by B3's purity test).
- **Accept:** A known AddColor stack (source via archive.org, add to
  `docs/samples.md`) renders colored; B&W goldens unchanged.

### D5. StuffIt method 5 = LZAH, not plain LZSS  [model: fable] [status: pending]

- **Goal:** Close F10(a). The current `DecompressLzss` emits garbage for
  method 5 because LZAH couples LZSS with adaptive Huffman coding of
  literals/lengths (LHarc lh1 family).
- **Spec:** Clean-room from format documentation (The Unarchiver wiki, LHA
  lh1 descriptions); do not port GPL/LGPL sources verbatim. 4KB window,
  adaptive frequency tree with rebuild at cap, positions coded with the
  fixed prefix table. Implement the SIT entry CRC-16 check while here and
  validate all methods against it (it would have caught this bug).
- **Accept:** `BeavisEmulatorV2.sit` extracts; data fork (43,040 bytes)
  has STAK magic at offset 4; CRCs verify for every extractable sample.

### D6. StuffIt 5 extractor  [model: fable] [status: pending]

- **Spec:** New `StuffIt5Extractor` for the `StuffIt (c)1997-` container:
  80-byte archive header, variable-length entry headers, methods 0/13
  first; method 15 (Arsenic: BWT + MTF + RLE + arithmetic coder) as a
  separately-committed follow-up; until then enumerate the file table and
  report per-file unsupported methods. Register ahead of the classic
  extractor.
- **Accept:** `ContextualMenus.sit` extracts and renders, or (pre-Arsenic)
  lists its contents with a per-file "method 15 unsupported" message.

### D7. Embedded font resources (FOND/NFNT)  [model: fable] [status: pending]

- **Spec:** When a user's stack carries FOND/NFNT bitmap fonts, decode the
  NFNT strike (bit image, location table, offset/width table) and render
  with the *actual* embedded font at native size, as tier 0 above the
  existing FontMapper tiers. Structure as an `IGlyphSource` abstraction
  over (TTF typeface | NFNT strike) so `TextRenderer` stays
  single-purpose. Fully within Asset Policy (user-supplied data); this is
  the maximum-fidelity endgame for text.
- **Accept:** Fixture stack with embedded NFNT renders using it (compare
  against an emulator screenshot); stacks without embedded fonts
  unaffected.

### D8. QuickTime MOV via LibVLC  [model: sonnet] [status: pending]

- **Spec:** Original plan Phase 9: `MediaService` wrapping LibVLCSharp;
  embedded VideoView positioned over the card rect the script names;
  wire the `play movie` XCMD-style invocations found in real stacks
  (log-first: run samples, collect the exact calls, implement those).
- **Accept:** A stack referencing a MOV plays it in-window; missing
  codecs degrade to a log entry.

---

## 8. Phase E: System 7 shell chrome (after B and C)

The shell should read as a 1-bit Macintosh regardless of host OS (see the
B&W dialog conventions in `AGENTS.md`; the UI deliberately avoids gray
tones). Chrome work resumes once Phases A-C are done; card fidelity
outranks chrome, a lesson paid for in commit history.

### E1. Stabilize and modularize the chrome  [model: sonnet] [status: pending]

- **Spec:** Move `System7MenuBar`, `System7TitleBar`, and window styling
  into `App/Chrome/` with a README documenting the metrics (menu bar
  height, title-bar rake pattern, border widths) and the known platform
  landmines from commit history (window transparency + CornerRadius broke
  Windows launch twice). Add an Avalonia headless smoke test that
  MainWindow materializes, run on the CI OS matrix.
- **Accept:** Headless test green on Windows/macOS/Linux CI; one code
  path, no per-OS visual divergence beyond what the README documents.

### E2. HyperCard furniture  [model: sonnet] [status: partial]

- **Spec:** Go menu exists (verify Back/Home/Recent semantics); visual
  effects exist in `TransitionRenderer` (verify: dissolve, wipe l/r/u/d,
  iris open/close, checkerboard, timed per fast/slow qualifiers, driven
  by `visual effect` before `go`). Add the message box (`msg`): a
  single-line System 7 window that accepts and evaluates one line of
  HyperTalk; `put` without a target writes to it.
- **Accept:** `visual effect dissolve` + `go next card` animates; message
  box round-trips `the number of cards`.

### E3. Dialog styling sweep  [model: haiku] [status: pending]

- **Spec:** Audit every dialog against the B&W System 7 conventions table
  in `AGENTS.md` (pure black/white, no grays, correct default-button
  ring); ensure keyboard completeness (Return = default, Esc = cancel).
- **Accept:** Screenshot checklist per dialog in the PR description.

---

## 9. Research track (ongoing, independent)

- **R1. HC 1.x format divergences** [fable]: version switch off the STAK
  `FormatVersion`; acquire 1.x samples; document deltas in
  `docs/stack-format.md`. AGENTS.md targets HC 2.4.1 as primary; 1.x
  detect-and-warn is the interim behavior.
- **R2. PICT decoder completion** [fable]: `Rendering/PictDecoder.cs`
  exists (~490 lines); audit opcode coverage against real stack PICTs,
  log unknown opcodes with offsets, extend as needed by D4.
- **R3. Password-protected stacks** [fable]: detect and report per
  AGENTS.md; decryption without the password is out of scope.
- **R4. Built-in icon replacement art** [human + haiku]: original 32x32
  pixel art for the ~40 most-referenced built-in icon IDs; wire into
  D2's table. Human judgment on art style and licensing.

---

## 10. Standing verification checklist (every task)

1. `dotnet build`: zero errors, no new warnings beyond the known
   `RawStackScanner.cs` CS8600.
2. `dotnet test`: green; new behavior has a unit test or golden image.
3. Render harness (A0) against all six samples: no crashes, clean logs.
4. Net-diff review per the Regression Check Policy in `AGENTS.md`.
5. Update `docs/stack-format.md` / `docs/hypertalk-coverage.md` when
   format or language knowledge changed.
6. Commit per Commit Policy (imperative message, Copilot co-author
   trailer). Pull at session start and before push: work happens from
   more than one machine, and a stale clone once produced a diagnosis of
   already-fixed bugs.
