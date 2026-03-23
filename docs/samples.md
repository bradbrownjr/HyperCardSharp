# Sample Files

The `samples/` directory is listed in `.gitignore` and is not committed to the repository.
Use the commands below to download all sample files to a fresh development environment.

## Downloading

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

## File Index

| File | Format | Source |
|------|--------|--------|
| `NEUROBLAST_Cyberdelia.sit` | StuffIt (.sit) | [archive.org](https://archive.org/details/hypercard_neuroblast-cyberdelia) |
| `NEUROBLAST_Cyberdelia.img` | DiskCopy 4.2 (.img) | [archive.org](https://archive.org/details/hypercard_neuroblast-cyberdelia) |
| `NEUROBLAST_HyperCard` | Raw HyperCard stack | [archive.org](https://archive.org/details/hypercard_neuroblast-hypercard-diskzine) |
| `NEUROBLAST_HyperCard.img` | DiskCopy 4.2 (.img) | [archive.org](https://archive.org/details/hypercard_neuroblast-hypercard-diskzine) |
| `ContextualMenus.sit` | StuffIt (.sit) | [archive.org](https://archive.org/details/hypercard_contextualmenus) |
| `BeavisEmulatorV2.sit` | StuffIt (.sit) | [archive.org](https://archive.org/details/hypercard_beavis-emulator-version-20) |

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
