# Sample Files

The `samples/` directory is listed in `.gitignore` and is not committed to the repository.
Use the commands below to download all sample files to a fresh development environment.

## Downloading

### Core Development Samples

```bash
mkdir -p samples
cd samples

# NEUROBLAST — Cyberdelia (StuffIt archive + disk image)
curl -L -o NEUROBLAST_Cyberdelia.sit   "https://archive.org/download/hypercard_neuroblast-cyberdelia/NEUROBLAST_Cyberdelia.sit"
curl -L -o NEUROBLAST_Cyberdelia.img   "https://archive.org/download/hypercard_neuroblast-cyberdelia/disk.img"

# NEUROBLAST — HyperCard Diskzine (raw stack + disk image)
curl -L -o NEUROBLAST_HyperCard        "https://archive.org/download/hypercard_neuroblast-hypercard-diskzine/NEUROBLAST_HyperCard"
curl -L -o NEUROBLAST_HyperCard.img    "https://archive.org/download/hypercard_neuroblast-hypercard-diskzine/disk.img"

# ContextualMenus (StuffIt archive)
curl -L -o ContextualMenus.sit         "https://archive.org/download/hypercard_contextualmenus/ContextualMenus.sit"

# Beavis Emulator v2 (StuffIt archive)
curl -L -o BeavisEmulatorV2.sit        "https://archive.org/download/hypercard_beavis-emulator-version-20/BeavisEmulatorV2.sit"
```

### Compatibility Test Suite

These stacks are selected to stress-test different subsystems. Each exercises a distinct combination of features.

```bash
# 1. Caper in the Castro — adventure game (HC 1.2, graphics, animation, sound, scripting)
#    Tests: HC 1.x format, MacBinary wrapper, complex scripted navigation, sound resources
curl -L -o CaperinTheCastro.bin        "https://archive.org/download/hypercard_caper-in-the-castro/Caper-in-the-Castro.bin"
curl -L -o CaperinTheCastro.img        "https://archive.org/download/hypercard_caper-in-the-castro/disk.img"

# 2. HyperGames 1.4 — 60+ mini-games with AddColor (HC 2.x, BinHex)
#    Tests: AddColor rendering, pure HyperTalk scripting (no XCMDs except AddColor),
#           large stack, many cards, color + B&W dual mode, BinHex container format
curl -L -o HyperGames.hqx             "https://archive.org/download/hypercard_hypergames/hyperGames.hqx"
curl -L -o HyperGames.img             "https://archive.org/download/hypercard_hypergames/disk.img"

# 3. Get Rich Quick — full-length adventure game (StuffIt archive)
#    Tests: StuffIt extraction, animated sequences, sound resources, multiple endings,
#           complex scripted puzzles, item inventory logic
curl -L -o GetRichQuick.sit            "https://archive.org/download/hypercard_get-rich-quick/Get_Rich_Quick_f.sit"
curl -L -o GetRichQuick.img            "https://archive.org/download/hypercard_get-rich-quick/disk.img"

# 4. Camping Story — color interactive children's story (StuffIt archive)
#    Tests: Color rendering, sound resources, interactive clickable regions,
#           personalized text insertion (ask/put into field), styled text
curl -L -o CampingStory.sit            "https://archive.org/download/hypercard_acampingstory/aCampingStory.sit"
curl -L -o CampingStory.img            "https://archive.org/download/hypercard_acampingstory/disk.img"

# 5. U.S. Map — educational reference (StuffIt archive)
#    Tests: Click-based hit testing on graphic regions, data lookup (state → capital/bird/flower),
#           dynamic text display in fields, find command, many cards
curl -L -o USMap.sit                   "https://archive.org/download/hypercard_us-map/U.S.%20Map.sit"
curl -L -o USMap.img                   "https://archive.org/download/hypercard_us-map/disk.img"

# 6. Roget's Thesaurus v1.0 — large text database (raw stack, 1.6 MB)
#    Tests: Large card count, find/search across many cards, text fields with
#           hypertext-style links, field locking, clipboard operations, raw stack format
curl -L -o RogetsThesaurus             "https://archive.org/download/hypercard_rogets-thesaurus-v10/Rogets-Thesaurus-v.1.0"
curl -L -o RogetsThesaurus.img         "https://archive.org/download/hypercard_rogets-thesaurus-v10/disk.img"

# 7. Hyper-Wordle — modern tutorial stack (StuffIt archive, 2022)
#    Tests: Contemporary HC 2.x stack, heavy HyperTalk logic (word validation,
#           letter-by-letter comparison), dynamic button hilite/color, repeat loops
curl -L -o HyperWordle.sit             "https://archive.org/download/hypercard_hyper-wordle/Hyper-Wordle.sit"
curl -L -o HyperWordle.img             "https://archive.org/download/hypercard_hyper-wordle/disk.img"

# 8. Mortal Wombat II — fighting game parody (StuffIt archive)
#    Tests: Visual effects/transitions, animated card sequences, sound effects,
#           character selection logic, game state via global variables
curl -L -o MortalWombatII.sit          "https://archive.org/download/hypercard_mortal-wombat-ii/mortal_wombat_ii.sit"
curl -L -o MortalWombatII.img          "https://archive.org/download/hypercard_mortal-wombat-ii/disk.img"
```

## File Index

### Core Samples

| File | Format | Source |
|------|--------|--------|
| `NEUROBLAST_Cyberdelia.sit` | StuffIt (.sit) | [archive.org](https://archive.org/details/hypercard_neuroblast-cyberdelia) |
| `NEUROBLAST_Cyberdelia.img` | DiskCopy 4.2 (.img) | [archive.org](https://archive.org/details/hypercard_neuroblast-cyberdelia) |
| `NEUROBLAST_HyperCard` | Raw HyperCard stack | [archive.org](https://archive.org/details/hypercard_neuroblast-hypercard-diskzine) |
| `NEUROBLAST_HyperCard.img` | DiskCopy 4.2 (.img) | [archive.org](https://archive.org/details/hypercard_neuroblast-hypercard-diskzine) |
| `ContextualMenus.sit` | StuffIt (.sit) | [archive.org](https://archive.org/details/hypercard_contextualmenus) |
| `BeavisEmulatorV2.sit` | StuffIt (.sit) | [archive.org](https://archive.org/details/hypercard_beavis-emulator-version-20) |

### Compatibility Test Suite

| File | Format | Year | What it tests |
|------|--------|------|---------------|
| `CaperinTheCastro.bin` | MacBinary (.bin) | 1989 | HC 1.x format, scripted adventure, sound, animation |
| `CaperinTheCastro.img` | Disk image (.img) | 1989 | HFS extraction of HC 1.x stack |
| `HyperGames.hqx` | BinHex (.hqx) | 1998 | AddColor, 60+ mini-games, pure HyperTalk, BinHex container |
| `HyperGames.img` | Disk image (.img) | 1998 | Large multi-game color stack |
| `GetRichQuick.sit` | StuffIt (.sit) | ~1996 | StuffIt, adventure game, sounds, animation, puzzles |
| `GetRichQuick.img` | Disk image (.img) | ~1996 | Full adventure game from disk image |
| `CampingStory.sit` | StuffIt (.sit) | ~1997 | Color, sound, ask/put personalization, children's interactive |
| `CampingStory.img` | Disk image (.img) | ~1997 | Color story stack from disk image |
| `USMap.sit` | StuffIt (.sit) | ~1996 | Hit-test on graphics, data lookup, find command |
| `USMap.img` | Disk image (.img) | ~1996 | Educational reference from disk image |
| `RogetsThesaurus` | Raw stack | ~1997 | Large card count, find/search, hypertext, raw format |
| `RogetsThesaurus.img` | Disk image (.img) | ~1997 | Large text database from disk image |
| `HyperWordle.sit` | StuffIt (.sit) | 2022 | Modern HC 2.x, heavy scripting logic, dynamic hilite |
| `HyperWordle.img` | Disk image (.img) | 2022 | Contemporary tutorial stack |
| `MortalWombatII.sit` | StuffIt (.sit) | ~1995 | Visual effects, animation, sounds, game state globals |
| `MortalWombatII.img` | Disk image (.img) | ~1995 | Fighting game from disk image |

## Feature Coverage Matrix

| Feature | Caper | HyperGames | GetRich | Camping | USMap | Roget | Wordle | Wombat |
|---------|-------|------------|---------|---------|-------|-------|--------|--------|
| HC 1.x format | **X** | | | | | | | |
| HC 2.x format | | **X** | **X** | **X** | **X** | **X** | **X** | **X** |
| AddColor | | **X** | | **X** | | | | |
| Sound resources | **X** | **X** | **X** | **X** | | | | **X** |
| Visual effects | **X** | **X** | **X** | | | | | **X** |
| Scripted navigation | **X** | **X** | **X** | **X** | **X** | **X** | **X** | **X** |
| Find/search | | | | | **X** | **X** | | |
| Ask/answer dialogs | | **X** | | **X** | | | | |
| Global variables | | **X** | **X** | | | | **X** | **X** |
| Repeat loops | | **X** | **X** | | | | **X** | |
| Button hilite | | **X** | | | **X** | | **X** | **X** |
| Field text mutation | | **X** | | **X** | **X** | | **X** | |
| Large card count | | **X** | **X** | | | **X** | | |
| MacBinary container | **X** | | | | | | | |
| BinHex container | | **X** | | | | | | |
| StuffIt container | | | **X** | **X** | **X** | | **X** | **X** |
| Disk image container | **X** | **X** | **X** | **X** | **X** | **X** | **X** | **X** |
| Raw stack format | | | | | | **X** | | |

## UI Reference Material

Links used for System 7.5 dialog and UI styling research:

- [Classic Mac OS Design Evolution — Version Museum](https://www.versionmuseum.com/history-of/classic-mac-os)
  Screenshots of every Mac OS version including System 7 (1991) and System 7.5 (1994).
- [Vintage Programming on Macintosh System 7.5 — Jan Kammerath](https://medium.com/@jankammerath/vintage-programming-on-macintosh-system-7-5-with-think-c-resedit-5d05c23a8016)
  Practical walkthrough with THINK C and ResEdit; useful for dialog and resource conventions.

## Notes

- The two `disk.img` files are renamed to match their corresponding `.sit` archives
  (both originate from their respective archive.org collection pages).
- `NEUROBLAST_HyperCard` has no extension — it is a raw STAK binary.
- All files are sourced from the Internet Archive and are freely redistributable
  for preservation purposes.
