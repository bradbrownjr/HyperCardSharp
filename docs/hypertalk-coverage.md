# HyperTalk Coverage

Tracks implementation status of HyperTalk commands, functions, and language features in HyperCard#.

**Legend:** ✅ Implemented | ⚠️ Partial | ❌ Stub / not implemented

---

## Navigation Commands

| Command | Status | Notes |
|---------|--------|-------|
| `go next` | ✅ | |
| `go prev` / `go previous` | ✅ | |
| `go first` | ✅ | |
| `go last` | ✅ | |
| `go back` | ✅ | Aliased to prev |
| `go forth` | ✅ | Aliased to next |
| `go to card N` | ✅ | 1-based index |
| `go to card "name"` | ✅ | Case-insensitive |
| `go to card id N` | ✅ | Block ID lookup |
| `go to stack "name"` | ✅ | Fires `CrossStackNavigationRequested` event |
| `go home` | ✅ | Opens file picker |
| `go to card of bg` | ❌ | Background-scoped card reference not parsed |

---

## Control Flow

| Statement | Status | Notes |
|-----------|--------|-------|
| `if … then … end if` | ✅ | |
| `if … then … else … end if` | ✅ | |
| `repeat forever … end repeat` | ✅ | 100 000 iteration safety cap |
| `repeat N times … end repeat` | ✅ | |
| `repeat while cond … end repeat` | ✅ | |
| `repeat until cond … end repeat` | ✅ | |
| `repeat with x = m to n … end repeat` | ✅ | |
| `exit repeat` | ✅ | |
| `exit <handlerName>` | ✅ | |
| `exit to HyperCard` | ✅ | |
| `next repeat` | ✅ | |
| `pass <handlerName>` | ✅ | Parsed; runtime bubbles up message |
| `return [expr]` | ✅ | Stored in `ReturnValue` |
| `do <script>` | ✅ | Wraps in anonymous handler and executes |
| `send <msg> [to <target>]` | ⚠️ | Send to target resolves script; send without target logs only |

---

## Data / Variables

| Statement | Status | Notes |
|-----------|--------|-------|
| `put <expr> into <container>` | ✅ | |
| `put <expr> before <container>` | ✅ | |
| `put <expr> after <container>` | ✅ | |
| `put <expr> into field "name"` | ✅ | |
| `put <expr> into char/word/item/line N of <var>` | ⚠️ | Simple chunk assignment works; deeply nested chunk targets not supported |
| `get <expr>` | ✅ | Stores in `it` |
| `global <varList>` | ✅ | |

---

## Set Property

| Pattern | Status | Notes |
|---------|--------|-------|
| `set hilite of button X to val` | ✅ | |
| `set text of field X to val` | ✅ | |
| `set visible of part X to val` | ✅ | |
| `set name of part X to val` | ✅ | |
| `set enabled of part X to val` | ✅ | |
| `set textFont of part X to val` | ✅ | Mac font ID or name |
| `set textSize of part X to val` | ✅ | |
| `set textStyle of part X to val` | ✅ | bold/italic/plain flags |
| `set rect/rectangle of part X to val` | ✅ | |
| `set loc/location of part X to val` | ✅ | |
| `set width/height of part X to val` | ✅ | |
| `set style of part X to val` | ✅ | |
| `set textColor of part X to val` | ✅ | |
| `set script of X to val` | ❌ | |
| `set userLevel to N` | ❌ | |
| `set cursor to N` | ❌ | |
| `set the blindTyping to val` | ❌ | Global environment properties |

---

## UI / Interaction

| Command | Status | Notes |
|---------|--------|-------|
| `answer <msg>` | ✅ | Dialog shown; button responses not captured |
| `answer <msg> with btn1 [or btn2 …]` | ⚠️ | Shown, buttons not read back |
| `ask <prompt> [with <default>]` | ✅ | Result stored in `it` |
| `click at <x,y>` | ✅ | Synthesises mouseUp at card coordinates |
| `type <text>` | ✅ | Appended to focused field |
| `wait <n> [ticks|seconds|milliseconds]` | ✅ | Hard cap 5 000 ms |
| `show <part>` | ✅ | Sets `Visible = true` |
| `hide <part>` | ✅ | Sets `Visible = false` |
| `show cards` / `show all cards` | ❌ | |
| `choose tool` | ❌ | Author-mode tool selection |
| `drag from … to …` | ❌ | |
| `open [file]` | ❌ | |
| `close [file]` | ❌ | |
| `print card` | ❌ | |

---

## Sound / Media

| Command | Status | Notes |
|---------|--------|-------|
| `play <soundName>` | ✅ | Looks up `snd ` resource, decodes to WAV, plays via LibVLC |
| `play "boing"` (system sounds) | ⚠️ | Only plays if a `snd ` resource named "boing" exists in the stack |
| `stop sound` | ✅ | |
| `play movie / video` | ❌ | LibVLC stub in place; video layout not wired |

---

## Visual Effects

| Effect | Status | Notes |
|--------|--------|-------|
| `visual effect dissolve` | ✅ | |
| `visual effect wipe left/right/up/down` | ✅ | |
| `visual effect scroll left/right/up/down` | ✅ | |
| `visual effect iris open/close` | ✅ | |
| `visual effect barn door open/close` | ✅ | |
| `visual effect checkerboard` | ✅ | |
| `visual effect venetian blinds` | ✅ | |
| `visual effect zoom in/out` | ✅ | |
| `visual effect push left/right/up/down` | ✅ | |
| Speed qualifiers (`slowly`, `fast`, etc.) | ✅ | Scaler applied to transition frame count |

---

## Arithmetic Commands

| Command | Status |
|---------|--------|
| `add <expr> to <container>` | ✅ |
| `subtract <expr> from <container>` | ✅ |
| `multiply <container> by <expr>` | ✅ |
| `divide <container> by <expr>` | ✅ |

---

## Find / Search

| Command | Status | Notes |
|---------|--------|-------|
| `find "text"` | ✅ | Searches all fields; navigates to first matching card |
| `find whole "text"` | ⚠️ | Qualifier word parsed but ignored; behaves as plain `find` |
| `find chars "text"` | ⚠️ | As above |
| `find word "text"` | ⚠️ | As above |
| `find string "text"` | ⚠️ | As above |
| `find "text" in field X` | ❌ | `in field` scope not parsed |

---

## Chunk Expressions

| Chunk type | Read | Write |
|------------|------|-------|
| `char N of <container>` | ✅ | ✅ |
| `char N to M of <container>` | ✅ | ✅ |
| `word N of <container>` | ✅ | ✅ |
| `word N to M of <container>` | ✅ | ✅ |
| `item N of <container>` | ✅ | ✅ |
| `item N to M of <container>` | ✅ | ✅ |
| `line N of <container>` | ✅ | ✅ |
| `line N to M of <container>` | ✅ | ✅ |
| Nested chunks (`word N of line M of …`) | ❌ | ❌ |

---

## Built-in Functions

| Function | Status | Notes |
|----------|--------|-------|
| `length(s)` | ✅ | |
| `abs(n)` | ✅ | |
| `round(n)` | ✅ | |
| `trunc(n)` | ✅ | |
| `sqrt(n)` | ✅ | |
| `sin(n)` | ✅ | |
| `cos(n)` | ✅ | |
| `tan(n)` | ✅ | |
| `exp(n)` | ✅ | |
| `ln(n)` | ✅ | |
| `log2(n)` | ✅ | |
| `max(…)` | ✅ | Variadic |
| `min(…)` | ✅ | Variadic |
| `random(n)` | ✅ | Returns 1–n inclusive |
| `offset(needle, haystack)` | ✅ | 1-based; 0 if not found |
| `upper(s)` / `uppercase(s)` | ✅ | |
| `lower(s)` / `lowercase(s)` | ✅ | |
| `trim(s)` | ✅ | |
| `number of words in s` | ✅ | |
| `number of chars in s` | ✅ | |
| `number of lines in s` | ✅ | |
| `number of items in s` | ✅ | |
| `char N of s` | ✅ | |
| `atan(n)` | ❌ | |
| `exp2(n)` | ❌ | |
| `annuity(rate, periods)` | ❌ | Financial function |
| `compound(rate, periods)` | ❌ | Financial function |
| XCMDs / XFCNs | ❌ | Registry exists; no wiring from interpreter |

---

## Property References

| Property | Status | Notes |
|---------|--------|-------|
| `the date` | ✅ | M/d/yyyy format |
| `the time` | ✅ | h:mm:ss tt format |
| `the ticks` | ✅ | Uptime × 60 |
| `the seconds` | ✅ | Uptime in seconds |
| `the result` | ❌ | Always returns empty |
| `it` | ✅ | |
| `number of cards` | ✅ | |
| `number of card N` | ✅ | |
| `id of card` | ✅ | |
| `name of card` | ✅ | |
| `text of card` | ❌ | |
| `visible of part` | ❌ | Read always returns `true` |
| `text of field X` | ✅ | Via `GetFieldText` |
| `hilite of button X` | ✅ | Via `GetButtonHilite` |
| `the mouseH` / `the mouseV` | ❌ | Mouse position |
| `the mouse` | ❌ | Mouse button state |
| `the key` / `the keyCode` | ❌ | Keyboard state |
| `the clickLoc` | ❌ | |
| `message` / `the message box` | ❌ | |
| `the screenRect` | ❌ | |
| `the tool` | ❌ | |
| `the userLevel` | ❌ | |

---

## Message Passing

| Feature | Status | Notes |
|---------|--------|-------|
| `mouseUp` dispatch | ✅ | Button click → card → background → stack |
| `mouseDown` | ✅ | Parsed; dispatched on click |
| `openCard` / `closeCard` | ✅ | Fired on navigation |
| `openStack` / `closeStack` | ✅ | Fired on load / navigate away |
| `openBackground` / `closeBackground` | ✅ | Fired when background changes |
| `on <handlerName>` user handlers | ✅ | |
| `function <name>` user functions | ✅ | |
| Global variables (`global`) | ✅ | Shared across all scripts for session |
| `HyperCard` (top of hierarchy) | ❌ | No stack-level script container |
| System messages (`idle`, `mouseEnter`, etc.) | ❌ | |

---

## Operators

| Operator | Status |
|----------|--------|
| `+`, `-`, `*`, `/` | ✅ |
| `^` (power) | ✅ |
| `mod` | ✅ |
| `div` (integer division) | ✅ |
| `=`, `≠`, `<`, `>`, `≤`, `≥` | ✅ |
| `is`, `is not` | ✅ |
| `contains` | ✅ |
| `is in` | ✅ |
| `and`, `or`, `not` | ✅ |
| `&` (concat) | ✅ |
| `&&` (concat with space) | ✅ |
| Unary `-` | ✅ |

---

## Known Gaps / Contributor Opportunities

1. **Nested chunk expressions** — `word 2 of line 3 of field "data"` not evaluated (outer chunk only)
2. **`find in field X`** — scope not parsed; searches all fields
3. **Mouse/keyboard properties** — `the mouse`, `the key`, `clickLoc` not tracked
4. **System messages** — `idle`, `mouseEnter`, `mouseLeave`, `keyDown`, `tabKey`, `newCard`, etc.
5. **Stack-level script** — the STAK block script field is not parsed or dispatched into
6. **XCMD/XFCN wiring** — registry is in place but interpreter never calls into it
7. **`answer` button read-back** — `it` is not set to the chosen button label
8. **`open`/`close` file** — HyperTalk file I/O commands
9. **`the result`** — should reflect success/failure of last command
10. **`visible of part` read-back** — always returns `true` regardless of actual state
