# RenderDump

Headless diagnostic CLI for `Core` + `Rendering`. It opens any supported
input file (raw stack, `.sit`, `.img`), runs the full container-unwrap and
stack-parse pipeline, and either renders cards to PNG or prints a text
inventory. Every rendering and container-format task in `ROADMAP.md` cites
this tool's output as its acceptance evidence, so keep it in sync with the
`Core`/`Rendering` public APIs as they evolve.

## Usage

Render the first N cards to PNG files:

```
dotnet run --project tools/RenderDump -- <input-file> <out-dir> [max-cards]
```

Print the block inventory and a per-card part table without rendering:

```
dotnet run --project tools/RenderDump -- --info <input-file> [max-cards]
```

`max-cards` defaults to all cards when omitted. All container-pipeline log
lines (which extractor matched, how many stacks were found, resource fork
size) are echoed to stdout, which is usually the first thing worth reading
when a sample behaves unexpectedly.

## Linux native SkiaSharp assets

The umbrella `SkiaSharp` package does not ship Linux native binaries; without
`SkiaSharp.NativeAssets.Linux.NoDependencies` (pinned to the same version as
`SkiaSharp` in the `.csproj`), any `SKImageInfo`/`SKObject` use throws a type
initializer exception at runtime on Linux. This package is a no-op on
Windows/macOS builds.

## What this tool owns

Rendering samples to disk for manual inspection and for future golden-image
regression tests (`ROADMAP.md` task B7). It does not own test assertions
itself; it is a CLI, not a test project.

## What this tool must not do

Must not depend on `HyperCardSharp.App` or Avalonia; it exists precisely so
rendering can be inspected without launching the desktop UI.
