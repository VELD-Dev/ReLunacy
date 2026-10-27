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
