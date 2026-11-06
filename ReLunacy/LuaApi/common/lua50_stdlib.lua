---@meta
--- Lua 5.0 standard library, limited to what the shipped scripts actually call.
---
--- This file is accurate rather than aspirational: it documents the reference 5.0 library, not the
--- game's own API. Note table.getn and table.foreach - both exist only in 5.0, 5.1 replaced them
--- with the # operator, which is one more confirmation that these games are 5.0.
---
--- Everything the GAME exposes goes in the per-game folder next to this one, because none of it can
--- be derived from the scripts: Lua only ever calls those functions, never defines them.

---Writes its arguments to the console, separated by tabs.
---@param ... any
function print(...) end

---Converts a value to a number, or nil when it cannot be.
---@param value any
---@param base? number radix between 2 and 36; defaults to 10
---@return number|nil
function tonumber(value, base) end

---Iterator over every key of a table, in unspecified order.
---@param t table
---@return function iterator
---@return table state
---@return nil control
function pairs(t) end

---Iterator over a table's 1..n integer keys, stopping at the first nil.
---@param t table
---@return function iterator
---@return table state
---@return number control
function ipairs(t) end

---@class stringlib
string = {}

---Length of a string in bytes.
---@param s string
---@return number
function string.len(s) end

---Searches for a pattern. Returns the start and end indices, then any captures, or nil.
---@param s string
---@param pattern string
---@param init? number index to start at; negative counts from the end
---@param plain? boolean treat the pattern as plain text rather than a pattern
---@return number|nil start
---@return number|nil finish
function string.find(s, pattern, init, plain) end

---Substring between two indices, both inclusive, negatives counting from the end.
---@param s string
---@param i number
---@param j? number defaults to -1, the last byte
---@return string
function string.sub(s, i, j) end

---@class mathlib
math = {}

---Largest integer not greater than x.
---@param x number
---@return number
function math.floor(x) end

---A pseudo-random number: in [0,1) with no arguments, an integer in [1,m] with one, in [m,n] with two.
---@param m? number
---@param n? number
---@return number
function math.random(m, n) end

---@class tablelib
table = {}

---Inserts a value, at the end by default or at a given position.
---@param t table
---@param pos? number
---@param value? any
function table.insert(t, pos, value) end

---Number of elements in a sequence. Lua 5.0 only - 5.1 replaced this with the # operator.
---@param t table
---@return number
function table.getn(t) end

---Sorts a sequence in place, by < unless a comparison function is given.
---@param t table
---@param comp? function
function table.sort(t, comp) end

---Calls f(key, value) for every pair. Lua 5.0 only; deprecated in 5.1.
---@param t table
---@param f function
function table.foreach(t, f) end
