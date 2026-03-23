# GitHub Copilot Instructions for HyperCard#

> Full project context lives in `AGENTS.md` at the repo root. Read it for architecture,
> engineering principles, planning rules, and the always/never memory protocol.

## Quick Reference

**What this is:** A cross-platform C# / .NET 8 HyperCard stack player and HyperTalk interpreter.
**UI:** AvaloniaUI (MVVM). **Media:** LibVLCSharp. **No database** — file-based (.stk parsing only).

## Copilot Behavior

- Follow all Engineering Principles and Root-Cause Policy defined in `AGENTS.md`.
- Before suggesting a large change, summarize the plan, open questions, and risks.
- Suggest graceful degradation for unsupported features — never let unknown stack content crash the app.
- When suggesting HyperTalk interpreter or stack parser code, keep the AST and parser decoupled.
- Prefer explicit, readable code over clever one-liners — this codebase is a research and preservation tool and must be understandable to contributors.
- If a HyperCard format behavior is uncertain, leave a `// TODO: verify against real stack` comment rather than guessing silently.
