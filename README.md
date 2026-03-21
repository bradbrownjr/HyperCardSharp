# HyperCardSharp

A cross-platform, open source HyperCard stack player and HyperTalk interpreter built with C# and .NET 8.

## Goals

- Open and render classic Mac HyperCard stacks (`.stk` files) natively on Windows, macOS, and Linux
- Interpret HyperTalk scripting with broad compatibility
- Play legacy QuickTime MOV files and audio via LibVLC
- Further reverse engineer the HyperCard binary stack format, closing known gaps left by existing tools
- Provide a foundation that the retro computing and preservation community can build on

## Status

Early development. Contributors welcome.

## Planned Features

- [x] Project scaffolding
- [ ] Stack binary parser (resource fork layout)
- [ ] Card/background/field/button renderer (AvaloniaUI)
- [ ] HyperTalk lexer, parser, and interpreter
- [ ] QuickTime MOV and audio playback (LibVLCSharp)
- [ ] PICT resource rendering
- [ ] XCMD/XFCN stub layer (graceful unsupported logging)
- [ ] HyperCard 1.x and 2.x format support

## Architecture

```
HyperCardSharp/
├── Core/
│   ├── StackParser/        # Binary stack format reader
│   ├── HyperTalk/          # Lexer, parser, interpreter
│   └── Resources/          # PICT, snd, icon resource parsers
├── Rendering/              # AvaloniaUI card/field/button rendering
├── Media/                  # LibVLCSharp QuickTime/audio wrapper
└── Xcmd/                   # XCMD/XFCN stub layer
```

## Known Stack Format Gaps

The HyperCard binary format is partially reverse-engineered. Known areas needing further research:

- `PICT` resource rendering (complex, inconsistent across versions)
- Styled text runs inside fields
- HyperCard 2.4 password encryption
- Some card layout flags
- HyperCard 1.x vs 2.x format divergences

Primary references:
- [HyperCardPreview by Pierre Lorenzi](https://github.com/PierreLorenzi/HyperCardPreview) — deepest existing binary format work
- [ViperCard](https://github.com/vipercard/vipercard) — browser-based HyperCard reimplementation
- [OpenXION](http://www.openxion.org/) — open source HyperTalk interpreter (Java)

## Requirements

- .NET 8 SDK
- LibVLC (installed via NuGet as LibVLCSharp)

## Contributing

This project is in its early stages and contributions of any size are welcome — especially:
- Stack format reverse engineering
- HyperTalk language edge cases
- Testing against real `.stk` files
- PICT rendering research

## License

MIT
