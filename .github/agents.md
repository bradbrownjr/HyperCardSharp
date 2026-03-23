# GitHub Copilot Agent Instructions for HyperCard#

> Full project context lives in `AGENTS.md` at the repo root. Read it before acting.

## Agent Behavior

- Always read `AGENTS.md` before beginning any multi-step task.
- Present a plan and get confirmation before implementing any feature or large refactor.
- Do not make sweeping changes across multiple files without a stated, agreed plan.
- When editing the stack parser or HyperTalk interpreter, run or describe a validation path against a known `.stk` file.

## Autonomous Task Scope

The agent may autonomously:
- Create or edit files within the established architecture (`Core/`, `Rendering/`, `Media/`, `Xcmd/`).
- Add unit tests for parser or interpreter logic.
- Update `AGENTS.md` with new always/never rules when instructed by the user.
- Document newly reverse-engineered stack format fields inline and in `AGENTS.md`.

The agent must stop and ask before:
- Changing the solution structure or project layout.
- Adding new NuGet dependencies.
- Modifying `.github/` workflows or CI configuration.
- Removing or renaming public types or interfaces.
