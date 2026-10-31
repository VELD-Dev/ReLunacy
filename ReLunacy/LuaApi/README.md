# Lua API documentation

These files tell the Lua editor what the game exposes: global functions, object types and their
methods, parameter names and types, return shapes and descriptions. **None of it can be derived from
the scripts.** The shipped bytecode makes 35,490 method calls while the 524 shipped sources contain
zero method definitions — Lua only ever *calls* the engine's API — so this is the one place that
knowledge can live.

Everything here is hand-written and meant to be edited. The editor reads it for completion,
signature popups and hover text.

## Layout

```
LuaApi/
  common/                  loaded for every game
  RFallOfMan/              loaded for that game only
  ToolsOfDestruction/
  QuestForBooty/
```

Folder names are `GameId` values. Those three are the games on LuaC b4404. A folder that is neither
`common` nor a game name is reported on load rather than silently ignored, since nothing in it would
be read. `common` plus the current game's folder are loaded together.

Files are ordinary Lua stubs with LuaLS / EmmyLua annotations, so an editor with the Lua extension
understands them too. Bodies stay empty — only the signatures and annotations are read.

## Tags

| Tag | Use |
|---|---|
| `---@class Name : Base` | declares an object type; `: Base` is optional |
| `---@field name type description` | a field on the class |
| `---@param name type description` | a parameter; `name?` marks it optional |
| `---@return type description` | one return value, repeat for several |
| `---@alias Name type` | a name for an existing type |
| `---@type type` | the type of a documented global variable |
| `---@meta` | marks the file as a stub; ignored |
| `---@deprecated` | ignored for now |

Plain `---` lines become the description. A `--` comment (two dashes) is ignored entirely and breaks
the annotation block.

## Example

```lua
---@meta

---A handle to a moby placed in the level.
---@class Moby : Handle
---@field id number The moby's class id.
local Moby = {}

---Whether the handle still points at a live moby.
---@return boolean
function Moby:is_valid() end

---Suspends the script for a number of seconds.
---@param seconds number
function wait(seconds) end

---@param target Moby
---@param angle number
---@param high? boolean spawn above the target
---@return Moby
function skydive_spawn_missile(target, angle, high) end

---The player.
---@type Moby
Ratchet1 = nil
```

A class can be held by a differently named variable, which is how the standard library is written —
`---@class stringlib` above `string = {}` means `function string.len(s)` lands on `stringlib`.

## What gets checked

The loader reports, rather than quietly producing no completions:

- an unknown tag, e.g. `@parm`
- a type that is not builtin, declared or aliased
- a `@param` naming an argument the stub's signature does not have
- a parameter of the stub that has no `@param`
- a method declared on a class that was never declared
- a class or function documented twice
- a syntax error in the stub itself

Those appear in the editor, so a mistake in a freshly written annotation surfaces immediately
instead of showing up later as missing suggestions.

## How documentation text is formatted

**Markdown.** Not HTML, and that follows from the protocol rather than from a LuaLS preference:
LuaLS is a language-server implementation, and LSP carries hover and completion documentation as a
`MarkupContent` whose `kind` is a `MarkupKind` — `"plaintext"` or `"markdown"`. There is no HTML
kind. LuaLS sends markdown, and writes markdown itself: the signature block in a hover is a fenced
` ```lua ` block.

So inside `---` comments, write markdown:

```lua
---Spawns a moby and returns a handle to it.
---
---The `class` argument is a moby class id, not a name. Use `moby_handle:new` to wrap an id you
---already have:
---
---```lua
---local m = spawn_moby(1234)
---if m:is_valid() then ... end
---```
---
---See also: [the class list](docs/reverse/tod_moby_classes.md)
---@param class integer
---@return Moby
function spawn_moby(class) end
```

HTML is not part of the contract. VS Code happens to render a small allowlisted subset inside
markdown, but LuaLS neither documents nor relies on that, and clients that render markdown down to
text show the tags literally or drop them. Do not use it.

Two wrinkles that catch people out:

- Consecutive `---` lines are **joined with newlines**, and markdown treats a single newline as a
  space. A paragraph break therefore needs a blank `---` line; a hard line break needs two trailing
  spaces.
- `---@param name string # description` descriptions are markdown too, as are the `---|` descriptions
  of an alias's literal values.

### What this editor renders

`DocMarkdown` parses a deliberate subset and `DocMarkdownView` draws it in the completion popup and
the hover tooltip:

| Written | Rendered |
|---|---|
| ` ```lua … ``` ` | a code block, coloured by the editor's own Lua colourer |
| `` `code` `` | inline, in the string colour |
| `**bold**`, `__bold__` | a brighter colour — ImGui's default font has one face, so there is no bold glyph |
| `*italic*`, `_italic_` | the class colour, for the same reason |
| `- item`, `1. item` | a bullet, indented by nesting |
| `# Heading` | the keyword colour, with a rule under it |
| `---`, `***` | a horizontal rule |
| `[label](target)` | the label in the function colour, the target on hover |

Tables, block quotes and reference-style links are **not** rendered — nothing in these annotations
uses them, and a half-working table renderer is worse than showing the source text. Anything
unrecognised is drawn as the plain text it is, so writing more markdown than this degrades to
readable rather than to mangled.
