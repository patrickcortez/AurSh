# Lua Plugin System

**AurShell** ships a built-in Lua subset interpreter for writing plugins.
No external Lua installation is required — the interpreter is compiled
directly into the `aursh` binary and evaluated in-process, so plugins
load in microseconds and have zero runtime dependencies.

---

## Quick start

```bash
# Scaffold a new plugin
aursh-plugin init my-tool

# Install it into the shell
aursh-plugin add ./my-tool

# Run it
my-tool hello

# List loaded plugins
aursh-plugin list

# Remove it
aursh-plugin del my-tool
```

The `init` command creates a directory with two files:

```
my-tool/
  plugin.json   ← manifest (name, version, entry point, commands)
  init.lua      ← Lua entry point, runs once at shell startup
```

## Plugin manifest (`plugin.json`)

```json
{
  "name": "my-tool",
  "version": "1.0.0",
  "author": "you",
  "description": "A custom AurShell plugin",
  "entry": "init.lua",
  "type": "lua",
  "invokable": true,
  "commands": ["my-tool"]
}
```

| Field         | Type       | Description |
|---------------|------------|-------------|
| `name`        | `string`   | Unique plugin identifier. Must match the directory name under `~/.aursh/plugins/`. |
| `version`     | `string`   | Semantic version string for display. |
| `author`      | `string`   | Author name. |
| `description` | `string`   | Short description shown in `aursh-plugin list`. |
| `entry`       | `string`   | Filename of the Lua script to execute on load. Relative to the plugin directory. |
| `type`        | `string`   | `"lua"` for Lua plugins, `"binary"` for native executables, `"fsharp"` for F# scripts. |
| `invokable`   | `bool`     | If `true`, the plugin's registered commands are callable from the shell prompt. |
| `commands`    | `string[]` | Command names this plugin provides. Used for binary/fsharp plugins; Lua plugins register commands dynamically via `aursh.register()`. |

## Plugin lifecycle

1. On shell startup, AurShell scans `~/.aursh/plugins/*/plugin.json`.
2. For each valid manifest with `"type": "lua"`, a fresh `LuaInterpreter`
   instance is created.
3. The `aursh` API table and `require()` function are injected into the
   interpreter's global scope.
4. The entry file (`init.lua`) is executed. At this point the plugin
   should call `aursh.register()` to register its commands.
5. When the user types a registered command, AurShell calls the Lua
   callback with the arguments packed into a table.

## Lua language reference

AurShell's Lua interpreter implements a practical subset of Lua 5.x.

### Types

| Type       | Literal examples                    |
|------------|-------------------------------------|
| `nil`      | `nil`                               |
| `boolean`  | `true`, `false`                     |
| `number`   | `42`, `3.14`, `0xFF`, `1e10`        |
| `string`   | `"hello"`, `'world'`, `[[long]]`    |
| `table`    | `{}`, `{1, 2, 3}`, `{key = "val"}` |
| `function` | `function(x) return x end`          |

All numbers are IEEE 754 double-precision floats. Strings are immutable
byte sequences with backslash escape support (`\n`, `\t`, `\r`, `\\`,
`\"`, `\'`, `\a`, `\b`, `\0`). Long strings (`[[...]]` and `[=[...]=]`
with any number of `=` signs) are supported for multi-line content.

### Truthiness

Only `nil` and `false` are falsy. Everything else — including `0` and
the empty string `""` — is truthy.

### Variables and scope

```lua
-- Global assignment (visible everywhere)
x = 10

-- Local declaration (visible only in the enclosing block)
local y = 20
local a, b, c = 1, 2, 3
```

Unresolved names read from the enclosing scope chain up to globals.
Assignments to undeclared names create globals.

### Operators

#### Arithmetic
| Op  | Meaning         | Example        |
|-----|-----------------|----------------|
| `+` | Addition        | `3 + 4` → `7`  |
| `-` | Subtraction     | `7 - 2` → `5`  |
| `*` | Multiplication  | `3 * 4` → `12` |
| `/` | Division        | `7 / 2` → `3.5`|
| `%` | Modulo          | `7 % 3` → `1`  |
| `^` | Exponentiation  | `2 ^ 10` → `1024` |
| `-` | Unary negation  | `-x`           |

#### Comparison
| Op   | Meaning                |
|------|------------------------|
| `==` | Equal                  |
| `~=` | Not equal              |
| `<`  | Less than              |
| `<=` | Less than or equal     |
| `>`  | Greater than           |
| `>=` | Greater than or equal  |

Comparisons between numbers use numeric ordering. Comparisons between
strings use lexicographic (byte) ordering. Comparing values of different
types (except with `==` / `~=`) raises an error.

#### Logical
| Op    | Behavior                                          |
|-------|---------------------------------------------------|
| `and` | Returns first operand if falsy, else second.      |
| `or`  | Returns first operand if truthy, else second.     |
| `not` | Returns `true` if operand is falsy, else `false`. |

Short-circuit evaluation: `and` and `or` do not evaluate the second
operand if the first determines the result.

#### String concatenation
```lua
local greeting = "Hello" .. " " .. "World"   -- "Hello World"
```

#### Length
```lua
local n = #"hello"         -- 5
local t = {10, 20, 30}
local len = #t             -- 3
```

### Strings

```lua
-- Single-quoted
local a = 'hello\nworld'

-- Double-quoted
local b = "tab\there"

-- Long strings (no escape processing)
local c = [[
This is a
multi-line string
]]

-- Long strings with matching equals
local d = [==[contains ]] inside]==]
```

### Tables

Tables are the single compound data structure. They combine arrays
(integer-keyed, 1-based) and dictionaries (string-keyed) in one value.

```lua
-- Array-style
local colors = {"red", "green", "blue"}
print(colors[1])   -- "red"
print(#colors)     -- 3

-- Dictionary-style
local config = {
    name = "aursh",
    version = "1.0",
    debug = false
}
print(config.name)       -- "aursh"
print(config["version"]) -- "1.0"

-- Mixed
local mixed = {
    "first",                  -- [1] = "first"
    key = "value",            -- ["key"] = "value"
    [42] = "answer",          -- [42] = "answer"
}

-- Nested
local nested = {
    inner = { x = 1, y = 2 }
}
print(nested.inner.x)  -- 1
```

#### Table mutation
```lua
local t = {}
t[1] = "a"
t.name = "foo"
t["key"] = "bar"

-- Remove by setting to nil
t.name = nil
```

### Control flow

#### if / elseif / else
```lua
if x > 10 then
    print("big")
elseif x > 5 then
    print("medium")
else
    print("small")
end
```

#### while
```lua
local i = 1
while i <= 10 do
    print(i)
    i = i + 1
end
```

#### Numeric for
```lua
-- for var = start, stop [, step] do ... end
for i = 1, 10 do
    print(i)          -- 1, 2, 3, ..., 10
end

for i = 10, 1, -1 do
    print(i)          -- 10, 9, 8, ..., 1
end

for i = 0, 1, 0.1 do
    print(i)          -- 0, 0.1, 0.2, ..., 1.0
end
```

#### Generic for
```lua
local fruits = {"apple", "banana", "cherry"}

-- ipairs: iterate array portion in order
for i, fruit in ipairs(fruits) do
    print(i, fruit)
end

-- pairs: iterate all key-value pairs
local config = {name = "aursh", version = "1.0"}
for k, v in pairs(config) do
    print(k .. " = " .. v)
end
```

#### break
```lua
for i = 1, 100 do
    if i > 10 then break end
    print(i)
end
```

### Functions

```lua
-- Named function (global)
function greet(name)
    return "Hello, " .. name
end

-- Local function
local function add(a, b)
    return a + b
end

-- Anonymous function (lambda)
local square = function(x) return x * x end

-- Variadic functions
function printf(fmt, ...)
    print(string.format(fmt, ...))
end

-- Multiple return values
function divmod(a, b)
    return math.floor(a / b), a % b
end
local q, r = divmod(17, 5)   -- q=3, r=2
```

#### Method syntax
```lua
local obj = { name = "widget" }

function obj:describe()
    -- 'self' is injected automatically
    return "I am " .. self.name
end

print(obj:describe())  -- "I am widget"
```

### Comments

```lua
-- Single-line comment

--[[
Multi-line
block comment
]]

--[==[
Block comment with
matching equals
]==]
```

## Standard library

### Global functions

| Function                      | Description |
|-------------------------------|-------------|
| `print(val, ...)`            | Prints values separated by tabs, followed by a newline. |
| `tostring(val)`              | Converts any value to its string representation. |
| `tonumber(val)`              | Converts a string to a number. Returns `nil` on failure. |
| `type(val)`                  | Returns the type name as a string (`"nil"`, `"boolean"`, `"number"`, `"string"`, `"table"`, `"function"`). |
| `error(msg)`                 | Raises an error with the given message. |
| `pcall(func, ...)`          | Calls `func` in protected mode. Returns `true, results...` on success or `false, error_message` on failure. |
| `pairs(table)`               | Returns an iterator over all key-value pairs (array + hash). |
| `ipairs(table)`              | Returns an iterator over array elements (1, 2, 3, ...). |
| `select(index_or_"#", ...)` | With `"#"`, returns the count of extra args. With a number, returns all args from that index onward. |
| `unpack(table)`              | Returns all array elements of a table as multiple values. |
| `require(modname)`           | Loads a Lua module from the plugin directory. Path is `<plugin_dir>/<modname>.lua` (dots become path separators). Cached after first load. |

### `string` library

| Function                           | Description |
|------------------------------------|-------------|
| `string.sub(s, i [, j])`         | Returns the substring from position `i` to `j` (inclusive, 1-based). Negative indices count from the end. |
| `string.len(s)`                   | Returns the length of the string in bytes. |
| `string.upper(s)`                 | Returns the string in uppercase. |
| `string.lower(s)`                 | Returns the string in lowercase. |
| `string.rep(s, n)`               | Returns `s` repeated `n` times. |
| `string.find(s, pattern [, init])` | Finds the first occurrence of `pattern` in `s` (plain substring search). Returns start and end positions, or `nil`. |
| `string.format(fmt, ...)`        | C-style format string. Supports `%s` (string), `%d` (integer), `%f` (float), `%x`/`%X` (hex), `%q` (quoted), `%%` (literal percent). |

### `table` library

| Function                           | Description |
|------------------------------------|-------------|
| `table.insert(t, value)`         | Appends `value` to the end of the array portion. |
| `table.insert(t, pos, value)`    | Inserts `value` at position `pos`, shifting elements up. |
| `table.remove(t [, pos])`        | Removes and returns the element at position `pos` (default: last). |
| `table.concat(t [, sep])`        | Concatenates all array elements into a string, separated by `sep` (default: `""`). |

### `math` library

| Function / Constant     | Description |
|-------------------------|-------------|
| `math.floor(x)`        | Rounds down to the nearest integer. |
| `math.ceil(x)`         | Rounds up to the nearest integer. |
| `math.abs(x)`          | Returns the absolute value. |
| `math.max(x, ...)`     | Returns the maximum of its arguments. |
| `math.min(x, ...)`     | Returns the minimum of its arguments. |
| `math.sqrt(x)`         | Returns the square root. |
| `math.random([m [, n]])` | With no args: random float in [0,1). With `m`: random int in [1,m]. With `m,n`: random int in [m,n]. |
| `math.pi`              | The constant π (3.14159...). |
| `math.huge`            | Positive infinity. |

## AurSh API (`aursh` table)

When a Lua plugin is loaded, AurShell injects a global `aursh` table
with functions for interacting with the shell:

### `aursh.register(name, callback)`

Registers a shell command. When the user types `name` at the prompt,
`callback` is invoked with a table of string arguments.

```lua
aursh.register("greet", function(args)
    if args[1] then
        aursh.print("Hello, " .. args[1] .. "!")
    else
        aursh.print("Hello, World!")
    end
    return 0  -- exit code
end)
```

The callback receives a 1-indexed table of string arguments and should
return an integer exit code (0 for success).

### `aursh.print(text, ...)`

Prints text to the terminal (through the BlackBox viewport if active).
Multiple arguments are separated by tabs.

```lua
aursh.print("Status:", "OK")
```

### `aursh.print_color(text, r, g, b)`

Prints text in an RGB color (24-bit true color).

```lua
aursh.print_color("Success!", 100, 230, 150)
aursh.print_color("Error!", 255, 80, 80)
aursh.print_color("Info", 100, 180, 255)
```

### `aursh.exec(command)`

Executes a shell command string through AurShell's executor pipeline.
Returns the exit code as a number.

```lua
local exit = aursh.exec("git status")
if exit ~= 0 then
    aursh.print("git failed!")
end
```

### `aursh.get_env(name)` / `aursh.set_env(name, value)`

Read and write shell environment variables.

```lua
local home = aursh.get_env("HOME")
aursh.set_env("MY_VAR", "hello")
```

### `aursh.get_alias(name)` / `aursh.set_alias(name, value)`

Read and write shell aliases.

```lua
aursh.set_alias("ll", "ls -la")
local current = aursh.get_alias("ll")  -- "ls -la"
```

### `aursh.get_cwd()`

Returns the shell's current working directory.

```lua
local cwd = aursh.get_cwd()
aursh.print("You are in: " .. cwd)
```

### `aursh.get_os()`

Returns the operating system name (`"Windows"`, `"Linux"`, `"macOS"`,
`"Termux"`).

```lua
if aursh.get_os() == "Windows" then
    aursh.exec("cls")
else
    aursh.exec("clear")
end
```

### `aursh.get_user()` / `aursh.get_host()`

Returns the current username and hostname.

```lua
aursh.print("Logged in as " .. aursh.get_user() .. "@" .. aursh.get_host())
```

## Module system (`require`)

Plugins can split code across multiple `.lua` files using `require()`.
Module paths are relative to the plugin directory, with dots as
directory separators:

```
my-plugin/
  plugin.json
  init.lua
  utils.lua
  lib/
    parser.lua
```

```lua
-- init.lua
local utils = require("utils")       -- loads utils.lua
local parser = require("lib.parser") -- loads lib/parser.lua
```

Modules are cached after their first load — calling `require("utils")`
a second time returns the cached result without re-executing the file.

## Plugin management commands

The `aursh-plugin` builtin provides full lifecycle management:

```bash
# List all installed plugins
aursh-plugin list

# Create a new plugin from template
aursh-plugin init my-plugin
aursh-plugin init my-fsharp-plugin fsharp   # F# template

# Install a plugin from a local directory
aursh-plugin add ./path/to/my-plugin

# Remove an installed plugin
aursh-plugin del my-plugin

# Hot-reload a plugin (unload + reload)
aursh-plugin update my-plugin

# Syntax-check a plugin without loading it
aursh-plugin debug my-plugin
aursh-plugin debug ./path/to/script.lua
```

## Example: a complete plugin

### `plugin.json`
```json
{
  "name": "weather",
  "version": "1.0.0",
  "author": "you",
  "description": "Display weather information",
  "entry": "init.lua",
  "type": "lua",
  "invokable": true,
  "commands": ["weather"]
}
```

### `init.lua`
```lua
local function show_help()
    aursh.print("Usage: weather [city]")
    aursh.print("  Displays weather info for a city")
    aursh.print("")
    aursh.print("Options:")
    aursh.print("  --help    Show this help message")
end

local function get_weather(city)
    aursh.print_color("Weather for " .. city, 100, 200, 255)
    aursh.print("─────────────────────────")

    local exit = aursh.exec("curl -s wttr.in/" .. city .. "?format=3")
    return exit
end

aursh.register("weather", function(args)
    if not args[1] or args[1] == "--help" then
        show_help()
        return 0
    end

    return get_weather(args[1])
end)

aursh.print_color("[plugin] weather loaded", 100, 100, 130)
```

### Usage
```bash
# Install
aursh-plugin add ./weather

# Run
weather London
weather "New York"
weather --help

# Remove
aursh-plugin del weather
```

## Differences from standard Lua

AurShell's Lua interpreter is a purpose-built subset. The following
features from standard Lua 5.x are **not supported**:

| Feature              | Status |
|----------------------|--------|
| `repeat...until`    | Not implemented |
| `goto` / labels     | Not implemented |
| Metatables          | Not implemented |
| Coroutines          | Not implemented |
| `io` library        | Not available (use `aursh.exec` for file operations) |
| `os` library        | Not available (use `aursh.get_os`, `aursh.exec`) |
| `debug` library     | Not available |
| `load` / `dofile`  | Not available (use `require`) |
| Pattern matching    | `string.find` does plain substring only |
| Bitwise operators   | Not implemented |

These omissions are intentional: the interpreter is designed for
lightweight shell plugin scripting, not general-purpose programming.
Heavy computation or system-level I/O should be delegated to external
commands via `aursh.exec()`.

## Safety

- **Loop guard**: All loops (`while`, `for`) are capped at 1,000,000
  iterations to prevent infinite loops from freezing the shell.
- **Error isolation**: Plugin errors are caught and printed to stderr;
  they never crash the shell process.
- **No filesystem access**: Plugins cannot read or write files directly.
  All side effects go through `aursh.exec()`, which runs commands in
  the normal shell pipeline with the same permissions as the user.
- **Sandboxed scope**: Each plugin gets its own interpreter instance
  with an isolated global scope. Plugins cannot interfere with each
  other's state.
