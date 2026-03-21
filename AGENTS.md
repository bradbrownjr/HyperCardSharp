# Agent Guide for HyperCardSharp

## Product Purpose

HyperCardSharp is a cross-platform HyperCard stack player and HyperTalk interpreter for retro computing enthusiasts and digital preservationists who want to open and interact with classic Mac HyperCard stacks (`.stk` files) on modern Windows, macOS, and Linux systems without requiring full Mac emulation.

## Technical Stack

- **Language:** C# 12 / .NET 8
- **UI Framework:** AvaloniaUI (cross-platform, MVVM)
- **Media Playback:** LibVLCSharp (QuickTime MOV, audio)
- **Target Platforms:** Windows 11, macOS, Linux
- **Distribution:** Self-contained .NET publish (no runtime install required)

## Architecture

```
HyperCardSharp/
├── Core/
│   ├── StackParser/        # Binary stack format reader (resource fork layout)
│   ├── HyperTalk/          # Lexer, parser, and interpreter for HyperTalk scripting
│   └── Resources/          # PICT, snd, icon, and other resource type parsers
├── Rendering/              # AvaloniaUI card, field, and button rendering
├── Media/                  # LibVLCSharp wrapper for QuickTime/audio playback
└── Xcmd/                   # XCMD/XFCN stub layer — logs unsupported calls, does not crash
```

## Known Stack Format Research Areas

The HyperCard binary format is partially reverse-engineered. These areas need further work:

- `PICT` resource rendering (complex, inconsistent across HC versions)
- Styled text runs inside fields (font/size/style spans)
- HyperCard 2.4 password encryption
- Undecoded card layout flags
- HyperCard 1.x vs 2.x format divergences
- Foreign language script system encodings

Primary references:
- [HyperCardPreview by Pierre Lorenzi](https://github.com/PierreLorenzi/HyperCardPreview)
- [ViperCard](https://github.com/vipercard/vipercard)
- [OpenXION](http://www.openxion.org/)

## Engineering Principles

- Keep the codebase **DRY** — no duplicated logic across parsers, renderers, or interpreters.
- Follow **SOLID** principles — especially single responsibility in the parser and interpreter layers.
- Keep solutions **KISS** — simple, explicit, and maintainable over clever.
- Favor clear architecture and extensibility over short-term shortcuts.
- Prefer cohesive refactors over layered quick fixes.

## Root-Cause Policy

- Never patch symptoms when resolving issues.
- Always research and identify the root cause before implementing a fix.
- Resolve root causes thoroughly, even when the correct fix is invasive.
- Maintain a strong foundation-first mindset for long-term maintainability.

## Planning and Collaboration Rules

- Treat every user question as requiring a direct answer.
- Do not treat questions as rhetorical.
- Answer user questions before making code changes.
- Before implementing a feature or large change, present a clear plan of action.
- Before implementing a feature or large change, present open questions that affect implementation.
- Before implementing a feature or large change, present risks or concerns.
- Before implementing a feature or large change, present suggestions and tradeoffs.
- For large changes, get alignment on the plan before implementation.

## Always/Never Memory Protocol

- If the user says to "always" or "never" do something, treat it as an instruction to update `AGENTS.md` with that rule.
- `AGENTS.md` is the shared memory for this project across all AI assistants.
- If an instruction is not written in `AGENTS.md`, assume it may be forgotten in future sessions.
- When adding an always/never rule, capture it as a clear, testable directive.

## User Experience Requirements

- Stacks open with a single file picker action — no configuration required to view a basic stack.
- Unsupported features (XCMDs, missing codecs, unknown resource types) degrade gracefully with a visible log entry, never a crash.
- HyperTalk script errors surface as readable messages, not raw exceptions.
- The player presents cards at authentic HyperCard resolution (512×342 base), with optional scaling.

## Implementation Expectations

- Preserve architectural consistency — parser, renderer, and interpreter remain decoupled.
- Keep the HyperTalk interpreter's AST explicit and testable.
- Ensure stack parsing is traceable — unknown blocks should be logged with offset and length, not silently skipped.
- Update documentation when behavior, architecture, or format research findings change.

## Definition of Done

- The change solves the validated root cause.
- The implementation aligns with DRY, SOLID, and KISS.
- Unsupported features degrade gracefully without crashing.
- Tests or validation steps cover the changed behavior where possible.
- Related documentation and format research notes are updated.
