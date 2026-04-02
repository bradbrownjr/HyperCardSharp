# HyperCard Stack Binary Format

Research notes on the HyperCard stack binary format as implemented in HyperCard# and verified against
real HC 2.4.1 stacks. All multi-byte fields are **big-endian**. Block offsets are given relative to the
start of each block (after the 16-byte block header).

Primary references:
- [Definitive Guide to HC Stack File Format](https://www.kreativekorp.com/swdownload/wildfire/HC%20FILE%20FORMAT%202010.TXT) — most comprehensive written reference
- [HyperCardPreview (Pierre Lorenzi, Swift)](https://github.com/PierreLorenzi/HyperCardPreview) — deepest reverse-engineering work; source for WOBA format
- [hypercard4net (giawa, C#)](https://github.com/giawa/hypercard4net) — partial C# reference
- [AddColor Resource Format](https://hypercard.org/addcolor_resource_format/)

---

## File Layout

A HyperCard stack is a flat binary file composed of a sequence of variable-length **blocks**.
Each block begins with a 16-byte header:

```
Offset  Size  Field
0x00    4     Block size (bytes, including this header)
0x04    4     Block type (4 ASCII chars, e.g. "STAK", "CARD")
0x08    4     Block ID (signed int32; unique within type)
0x0C    4     Reserved / filler (always 0 in HC 2.x)
```

The first block in the file is always `STAK` at file offset 0. The 4-byte type field at offset 4
is the file magic: `53 54 41 4B` ("STAK").

---

## Block Types

| Type | Name | Parsed? | Notes |
|------|------|---------|-------|
| `STAK` | Stack header | ✅ | First block; contains global metadata |
| `MAST` | Master index | ✅ | Offset table for all blocks |
| `LIST` | Card list | ✅ | B-tree root listing all cards in order |
| `PAGE` | Card list page | ✅ | B-tree leaf; card reference array |
| `CARD` | Card block | ✅ | Per-card data: parts, content, bitmap ref |
| `BKGD` | Background block | ✅ | Per-background data: parts, scripts |
| `BMAP` | Bitmap | ✅ | WOBA-compressed 1-bit bitmap |
| `STBL` | Style table | ✅ | Text style definitions |
| `FTBL` | Font table | ✅ | Font ID → name table |
| `PRNT` | Print settings | ❌ | Logging only |
| `PRST` | Print info | ❌ | Logging only |
| `PCFL` | Page control | ❌ | Logging only |
| `FREE` | Free block | ❌ | Unused space markers |
| `TAIL` | Tail | ❌ | End-of-file sentinel |

Unknown block types are logged with their type code, file offset, and byte size.

---

## STAK Block (`StackBlock`)

The STAK block contains global stack metadata. Format version at +0x10 identifies HC version.

```
Offset  Size  Field               Notes
0x00    4     Block size          (part of 16-byte header)
0x04    4     "STAK"              (part of 16-byte header)
0x08    4     Block ID            (part of 16-byte header)
0x0C    4     Reserved

0x10    4     Format version      7 = HC 1.x; 8 = HC 2.0; 9 = HC 2.1; 10 = HC 2.2–2.4.1
0x14    4     Total blocks        Count of blocks in file (including STAK)
0x18    4     Reserved
0x1C    4     List block ID       ID of the LIST block
0x20    4     Free block count
0x24    4     Free size           Total bytes in FREE blocks
0x28    4     Print rect          (4 x Int16: top, left, bottom, right)
0x30    4     Card count          Number of cards
0x34    4     Background count    Number of backgrounds
0x38    2     First background ID
0x3A    2     Max card ID
0x3C    4     Max background ID
0x40    4     Password hash       0 = no password; non-zero = HC 2.4 XOR hash
0x42    2     User level          1=Browse … 5=Scripting
0x44    2     Protection flags    bit 0 = can't modify; bit 1 = can't delete; bit 10 = private access
0x48    2     Protection flags 2
...
0x58    4     Card width          512 (0 = use default 512)
0x5C    4     Card height         342 (0 = use default 342)
0x60    8+    Patterns (16-byte)  Bitmap pattern table (8×8 pixels per entry, 40 entries)
```

**Format version → HyperCard version mapping:**

| Version | HC release |
|---------|-----------|
| 1 | HC 1.0 beta |
| 2 | HC 1.0 |
| 3 | HC 1.0.1 |
| 4 | HC 1.1 |
| 5 | HC 1.2 |
| 6 | HC 1.2.1–1.2.2 |
| 7 | HC 1.2.3–1.2.5 |
| 8 | HC 2.0 |
| 9 | HC 2.1 |
| 10 | HC 2.2–2.4.1 |

Versions ≤ 7 are treated as HyperCard 1.x; a warning is surfaced and rendering may be partial.

---

## MAST Block (`MasterBlock`)

Contains a flat array of 4-byte file offsets, one per possible block slot.
Non-zero entries point to the starting byte of a block in the file.
The array is indexed by a slot number derived from the block ID.

```
Offset  Size  Field
0x00    4×N   Offsets   Array of Int32 file offsets (0 = empty slot)
```

---

## LIST Block (`ListBlock`)

The root of the card ordering B-tree.

```
Offset  Size  Field
0x00    4     Page count          Number of PAGE blocks referenced
0x04    4     Total card count    Total cards across all pages
0x08    2     Card reference size Bytes per card reference entry in PAGE blocks (4 or 6)
0x0A    2     Reserved
0x0C    4×N   Page IDs            Array of PAGE block IDs
```

---

## PAGE Block (`PageBlock`)

A leaf in the card ordering B-tree. Contains an array of card references.

```
Offset  Size  Field
0x00    2     Reserved
0x02    2     Card count in this page
0x04    N×cardRefSize   Card references
```

Each card reference (size from LIST `cardRefSize`):

```
Offset  Size  Field
0x00    4     Card block ID
0x04    2     Flags (optional: low bit = marked card)
```

---

## CARD Block (`CardBlock`)

```
Offset  Size  Field
0x00    4     Bitmap block ID     0 = no bitmap
0x04    2     Flags               bit 14 = marked card
0x06    2     Reserved
0x08    4     Background block ID
0x0C    2     Part count
0x0E    2     Reserved
0x10    4     Part content count
0x14    ?     Parts               Variable-length array of part records
...           Part contents       Variable-length array
...           Name                Null-terminated Mac Roman string
...           Script              Null-terminated Mac Roman string
```

---

## BKGD Block (`BackgroundBlock`)

Same structure as CARD block except no bitmap block reference field (backgrounds share card bitmaps).

---

## BMAP Block (`BitmapBlock`)

Contains a WOBA-compressed 1-bit (monochrome) bitmap.

```
Offset  Size  Field
0x00    10    Reserved
0x0A    2     Top                 Dirty rect (bounding box of changed pixels)
0x0C    2     Left
0x0E    2     Bottom
0x10    2     Right
0x12    2     Mask rect top       (repeat of dirty rect in some HC versions)
...
0x1A    4     Mask data size      Bytes of compressed mask data (0 if no mask)
0x1E    4     Image data size     Bytes of compressed image data
0x22    ?     Mask data           WOBA-compressed mask
...           Image data          WOBA-compressed image
```

**WOBA Decompression** (`WobaDecoder`):

WOBA is a layer on top of PackBits with additional XOR-delta and row-padding steps:

1. **PackBits RLE** — standard Mac PackBits: `n ≥ 0` → copy n+1 literal bytes; `n < 0` (n ≠ −128`) → repeat next byte (1−n) times
2. **Row buffering** — each row is 72 bytes (512 pixels / 8 bits, padded to word, then 8 bytes extra)
3. **XOR delta** — each decoded row is XORed with the previous row to reconstruct absolute pixel values
4. **Dirty rect clipping** — pixels outside the dirty rect are white (1-bit meaning: 0 = black, 1 = white in Mac convention)

Full output is 512 × 342 pixels (72 bytes per row × 342 rows = 24,624 bytes) for the standard
Mac 128K/Plus/SE display.

---

## Part Record (in CARD / BKGD)

```
Offset  Size  Field
0x00    2     Part size           Total bytes including variable-length fields
0x02    2     Part ID             1-based within card/background
0x04    1     Part style flags    bit 7 = button (0) or field (1); other bits = style
0x05    1     Visibility + other flags   bit 7 = visible
0x06    2     Top
0x08    2     Left
0x0A    2     Bottom
0x0C    2     Right
0x0E    2     Feature flags       (font ID for fields; button style flags for buttons)
0x10    2     Text style flags    bold=0x01, italic=0x02, underline=0x04, outline=0x08, shadow=0x10, condense=0x20, extend=0x40
0x12    2     Text size           In points (e.g. 12)
0x14    2     Text align          0=left, 1=center, -1=right
0x16    ?     Name                Null-terminated Mac Roman string
...           Script              Null-terminated Mac Roman string (follows name + null)
```

---

## Part Content Record (in CARD / BKGD)

```
Offset  Size  Field
0x00    2     Part ID             (negative = background part content; positive = card part content)
0x02    2     Content length
0x04    ?     Content data        See below
```

Content format (within the content data):

- **Plain text**: first byte = 0x00, remainder = Mac Roman text
- **Styled text**: first two bytes form a UInt16 with bit 15 set
  - Bits 0–14 = formatting block size
  - Formatting block = array of style run records: `(charOffset UInt16, styleId UInt16)`
  - After formatting block = Mac Roman text body

---

## STBL Block (`StyleTableBlock`)

Array of text style entries. Each entry defines font, size, style, and alignment for a style run.

```
Offset  Size  Field
0x00    2     Style count
0x02    ?     Style records (variable width)
```

Each style record:

```
Offset  Size  Field
0x00    2     Style ID
0x02    2     Text height         Line height in pixels (0 = default)
0x04    2     Font ID             Mac font ID
0x06    2     Style flags         bold/italic/etc bitmask
0x08    2     Text size           In points
```

---

## FTBL Block (`FontTableBlock`)

Maps Mac font IDs to font name strings.

```
Offset  Size  Field
0x00    2     Font count
0x02    ?     Font records (variable width)
```

Each font record:

```
Offset  Size  Field
0x00    2     Font ID             Mac font ID (e.g. 3 = Geneva, 4 = Monaco, 21 = Palatino)
0x02    2     Reserved
0x04    ?     Font name           Null-terminated Mac Roman string, word-aligned
```

**Known Mac font IDs:**

| ID | Name |
|----|------|
| 0 | Chicago |
| 1 | Geneva (application font) |
| 2 | New York |
| 3 | Geneva |
| 4 | Monaco |
| 20 | Times |
| 21 | Helvetica |
| 22 | Courier |
| 23 | Symbol |
| 24 | Mobile (Alexandria) |

---

## Resource Fork Layout

Container formats (MacBinary, AppleSingle, HFS) carry a Mac resource fork alongside the data fork.
HyperCard stores sounds, icons, PICT images, and AddColor data in the resource fork.

```
Offset  Size  Field
0x00    4     Data offset         Offset from start of resource fork to resource data
0x04    4     Map offset          Offset to resource map
0x08    4     Data length
0x0C    4     Map length
0x10    112   Reserved (padding to 256 bytes)
...           Resource data section (raw resource bytes, each preceded by 4-byte length)
...           Resource map
```

**Resource Map:**

```
Offset  Size  Field
0x00    16    Copy of fork header (reserved)
0x10    4     Reserved
0x14    2     Resource fork attributes
0x16    2     Offset to type list (relative to map start)
0x18    2     Offset to name list (relative to map start)
...           Type list: (count−1 Int16), then per-type: (4-char type, count−1 Int16, offset Int16)
...           Reference list: per resource: (ID Int16, nameOffset Int16, attributes Byte, dataOffset 3-bytes)
...           Name list: per name: (length Byte, Pascal string bytes)
```

---

## Resource Types Used by HyperCard

| Type | Purpose | Parsed? |
|------|---------|---------|
| `snd ` | Mac sampled sound (Format 1 or 2) | ✅ `SoundDecoder` |
| `ICON` | 32×32 1-bit icon (128 bytes) | ✅ `IconDecoder` |
| `PICT` | QuickDraw picture | ⚠️ `PictDecoder` (common opcodes) |
| `HCcd` | Card-level AddColor regions | ✅ `AddColorDecoder` |
| `HCbg` | Background-level AddColor regions | ✅ `AddColorDecoder` |
| `XCMD` | External command (native code) | ❌ stub/log |
| `XFCN` | External function (native code) | ❌ stub/log |
| `WDGT` | Widget (HC 2.4 only) | ❌ |
| `STR ` | String resource | ❌ |
| `STR#` | String list | ❌ |
| `cicn` | Color icon | ❌ |
| `clut` | Colour lookup table | ❌ |

---

## AddColor Resource Format (`HCcd` / `HCbg`)

Used by the AddColor XCMD to store per-card or per-background color region data.

```
Offset  Size  Field
0x00    2     Record count
0x02    N×22  Color region records (22-byte variant)
```

Each color region record (22-byte standard variant):

```
Offset  Size  Field
0x00    2     Part ID             0 = card/background rect; >0 = specific part
0x02    2     Top
0x04    2     Left
0x06    2     Bottom
0x08    2     Right
0x0A    2     Fill red            QuickDraw RGBColor component (0–65535)
0x0C    2     Fill green
0x0E    2     Fill blue
0x10    2     Frame red
0x12    2     Frame green
0x14    2     Frame blue
```

A 14-byte variant (fill only, no frame) is auto-detected when `dataLen == count × 14`.
Color components are converted to 8-bit by dropping the low byte (right-shift 8).

---

## `snd ` Resource Format

Mac sampled sound resources. Two format variants:

**Format 1 (sndListResource):**
```
0x00  2   Format: 0x0001
0x02  2   Synthesizer count
0x04  2   Synthesizer type     5 = sampled sound synthesizer
0x06  2   Init options
0x08  2   Command count
0x0A  N×8 Command records: (command UInt16, param1 Int16, param2 Int32)
```

**Format 2 (sndResource):**
```
0x00  2   Format: 0x0002
0x02  2   Reference count
0x04  2   Command count
0x06  N×8 Command records
```

Scan commands for `bufferCmd` (0x8051) — `param2` is an offset into the resource to the sound header:

```
Sound header (offset from start of resource):
0x00  4   Sample pointer (ignored)
0x04  4   Sample count
0x08  10  Sample rate: 80-bit IEEE 754 extended (standard header) OR
          8-byte fields for extended/compressed headers
0x12  4   Loop start
0x16  4   Loop end
0x1A  1   Encode:  0x00 = standard (8-bit PCM), 0xFF = extended (16-bit), 0xFE = compressed
0x1B  1   Base frequency (MIDI note, e.g. 60 = middle C)
0x1C  ?   Sample data
```

HyperCard# outputs standard RIFF/WAV: PCM, 1 channel, sample rate from header, 8-bit unsigned (standard) or 16-bit signed (extended).

---

## PICT v2 Opcode Coverage

`PictDecoder` handles these opcodes found in real HC 2.4.1 stacks:

| Opcode | Name | Status |
|--------|------|--------|
| 0x0000 | NOP | ✅ |
| 0x0001 | ClipRegion | ✅ (skipped) |
| 0x0003 | TxFont | ✅ (skipped) |
| 0x0004 | TxFace | ✅ (skipped) |
| 0x0007 | PnSize | ✅ (skipped) |
| 0x000B | OvSize | ✅ (skipped) |
| 0x000D | TxSize | ✅ (skipped) |
| 0x001A | RGBFgCol | ✅ |
| 0x001B | RGBBkCol | ✅ |
| 0x001E | DefHilite | ✅ (skipped) |
| 0x001F | OpColor | ✅ (skipped) |
| 0x0020 | LineFrom | ✅ (skipped) |
| 0x0021 | Line | ✅ (skipped) |
| 0x0022 | ShortLine | ✅ (skipped) |
| 0x0023 | ShortLineFrom | ✅ (skipped) |
| 0x002C | LongText | ✅ (skipped) |
| 0x002E | DHDVText | ✅ (skipped) |
| 0x0028–0x002B | Text variants | ✅ (skipped) |
| 0x0030–0x0037 | Rect drawing | ✅ (skipped) |
| 0x0038–0x003F | SameRect drawing | ✅ (skipped) |
| 0x0040–0x0047 | RRect drawing | ✅ (skipped) |
| 0x0048–0x004F | SameRRect drawing | ✅ (skipped) |
| 0x0050–0x0057 | Oval drawing | ✅ (skipped) |
| 0x0058–0x005F | SameOval drawing | ✅ (skipped) |
| 0x0060–0x0067 | Arc drawing | ✅ (skipped with 8-byte data) |
| 0x0070–0x0077 | Polygon drawing | ✅ (skipped with size prefix) |
| 0x0080–0x0085 | Region variants | ✅ (skipped with size prefix) |
| 0x0090 | BitsRect | ✅ (skipped) |
| 0x0091 | BitsRgn | ✅ (skipped) |
| 0x0098 | PackBitsRect | ✅ Decoded — 1-bit B&W |
| 0x0099 | PackBitsRgn | ✅ (skipped) |
| 0x009A | DirectBitsRect | ✅ Decoded — 32-bit ARGB, 16-bit RGB5 |
| 0x009B | DirectBitsRgn | ✅ (skipped) |
| 0x00A0 | ShortComment | ✅ (2-byte kind, no data) |
| 0x00A1 | LongComment | ✅ (4-byte kind+size, skip data) |
| 0x00FF | EndPic | ✅ |
| 0x0011 | VersionOp | ✅ |
| 0x0C00 | HeaderOp | ✅ (12 bytes) |

Unknown opcodes use the PICT v2 range-based skip table rather than crashing.

---

## TODO / Verify against real stacks

- `// TODO: verify against real stack` — BMAP dirty rect field exact byte offset (may be +0x0C or +0x0A depending on HC version)
- STBL style entry total byte size (variable; word-aligned after name field)
- FTBL font record word-alignment rule (every record or only after odd-length names?)
- CARD/BKGD part content negative ID meaning — confirmed: negative = background part content referenced from a card block
- PAGE `cardRefSize` values seen in the wild: 4 bytes (HC 1.x / 2.0); 6 bytes (HC 2.1+)
- STAK `formatVersion` 10 confirmed in NEUROBLAST (HC 2.4.1); version 8 unverified
