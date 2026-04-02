# HyperCard#

**Open your old HyperCard stacks again — on Windows, Mac, or Linux.**

Remember HyperCard? The creative tool that shipped with every Macintosh, where anyone could build interactive stacks of cards with buttons, fields, graphics, sounds, and scripts — no programming degree required. Bill Atkinson's vision of "programming for the rest of us" inspired a generation of teachers, artists, students, and tinkerers.

HyperCard# brings those stacks back to life. Point it at a stack file and it just works — cards render, buttons click, scripts run, sounds play. No Mac emulator needed.

## Getting Started

1. Download the latest release for your platform (or build from source — see below)
2. Open HyperCard# and pick a stack file
3. Click through cards, press buttons, and explore — just like you remember

HyperCard# understands all the common ways stacks were shared back in the day: raw stack files, StuffIt archives (.sit), BinHex (.hqx), MacBinary (.bin), and Mac disk images (.img). Just open the file — the app figures out the format automatically.

## What Works

- **Cards and graphics** — black-and-white and color rendering, just like the original
- **Buttons and fields** — clickable, interactive, with styled text
- **HyperTalk scripts** — navigation, dialogs, loops, variables, visual effects, and more
- **Sounds** — embedded Mac sounds play back through your speakers
- **Visual effects** — dissolve, wipe, iris, scroll, checkerboard, and others
- **Disk images with multiple stacks** — a picker lets you choose which stack to open

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| Ctrl+O | Open a stack |
| Left / Right | Previous / next card |
| Home / End | First / last card |
| Ctrl+1 / 2 / 3 / 4 | Zoom level |
| Ctrl+M | Switch stack (disk images with multiple stacks) |
| Ctrl+H | Help |

## Building from Source

You'll need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed.

```bash
dotnet build
dotnet run --project src/HyperCardSharp.App
```

To create a standalone app that doesn't need .NET installed:

```bash
# Windows
dotnet publish src/HyperCardSharp.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# macOS (Apple Silicon)
dotnet publish src/HyperCardSharp.App -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true

# Linux
dotnet publish src/HyperCardSharp.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## Current Limitations

HyperCard# targets HyperCard 2.4.1 — the final Apple release. Most stacks from that era should work. A few things are still in progress:

- Some advanced HyperTalk commands aren't implemented yet
- PICT image resources may not render completely
- QuickTime movie playback isn't wired up yet
- HyperCard 1.x stacks are detected but may not render fully
- Password-protected stacks show a warning (decryption is out of scope)

If a stack uses a feature we don't support yet, the app will let you know gracefully — it won't crash.

## Contributing

Contributions are very welcome! The best ways to help:

- **Test with real stacks** — the more stacks we try, the more edge cases we find
- **HyperTalk script coverage** — there are still language features to implement
- **Format research** — the HyperCard binary format was never officially documented

Please file an issue before opening a large pull request so we can discuss approach.

## Technical Documentation

For developers and contributors:

- [HyperTalk command coverage](docs/hypertalk-coverage.md) — what's implemented, what's not
- [Stack binary format notes](docs/stack-format.md) — reverse-engineered format documentation
- [Development plan](docs/PLAN.md) — implementation roadmap and phasing
- [Agent guide](AGENTS.md) — architecture, conventions, and engineering principles

## License

MIT
