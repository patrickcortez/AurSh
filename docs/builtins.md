# Built-in Commands

**What it does**
AurSh ships with numerous built-in commands. These commands execute directly inside the shell process itself rather than spinning up external processes, making them extremely fast and allowing them to modify the shell's internal state.

There are two categories of built-ins: **Standard POSIX Built-ins** and **AurSh Native Tools**.

---

## AurSh Native Tools

These commands are prefixed with `aursh-` and provide advanced, TUI-driven (Terminal User Interface) capabilities that take advantage of AurSh's internal architecture.

### `aursh-cat` (TUI Text Editor & Reader)

**What it does**
It acts as a standard file reader, but when passed the `-e` flag, it transforms into a fast, vim-like interactive TUI text editor.

**Example Usage**
```bash
# Read a file to standard output
aursh-cat my_script.sh

# Open the interactive text editor
aursh-cat -e my_script.sh
```

**How it works internally**
When invoked normally, it streams the file directly to the console. When invoked with `-e`, it takes over the terminal buffer using ANSI escape sequences (`\x1b[?1049h`), loads the file into memory, and captures raw keystrokes (like `:` for commands or arrows for navigation) to let you edit the text in-place before saving it back to disk.

### `aursh-ls` (TUI File Explorer)

**What it does**
An interactive visual file explorer that lets you navigate your directories using your arrow keys. 

**Example Usage**
```bash
# Launch the explorer in the current directory
aursh-ls

# Launch the explorer in a specific path
aursh-ls /var/log/
```

**How it works internally**
It pauses the standard command prompt, draws a navigable list of files and folders using the terminal's raw input mode, and if you select a directory, it instructs the shell's internal working directory state to `cd` into that chosen path.

### `aursh-plugin`

**What it does**
Manages the installation, listing, and removal of Lua plugins and other tools to extend AurSh's functionality.

**Example Usage**
```bash
aursh-plugin list
aursh-plugin add ./my-tool
aursh-plugin del my-tool
```

**How it works internally**
It modifies the `.aursh/plugins` directory and triggers the `PluginManager` to dynamically load or unload the Lua interpreter contexts associated with those scripts.

### `aursh-assoc`

**What it does**
Associates a specific file extension with an executable compiler or interpreter, letting you run those scripts directly.

**Example Usage**
```bash
# Associate Python scripts
aursh-assoc .py "python \"{0}\" {1}"

# Now you can run it directly without typing 'python'
./script.py argument
```

**How it works internally**
It saves the association rule to the configuration. When the shell's `Parser` sees a file execution, it checks the extension, looks up the association rule, replaces `{0}` with the file path, and executes the substituted string.

### Other Native Tools
- `aursh-ssh`: A visual TUI for managing and connecting to remote SSH hosts and keys (see [AurSh SSH documentation](aursh-ssh.md)).
- `aursh-history`: Opens a visual TUI list of your past commands to search and execute them.
- `aursh-context`: Manages persistent disk-backed variables (see Contexts documentation).
- `aursh-net`: A cross-platform Wi-Fi manager and P2P file sender (see AurshNet documentation).
- `aursh-reload`: Instantly reloads the shell's configuration and plugins without restarting the terminal.
- `aursh-about`: Displays system info, version, and architecture details.

### `aursh-update`

**What it does**
Reaches out to the remote repository and updates the shell to the latest version. It safely stashes any uncommitted local changes you might have made in the shell's source directory so that updates never fail due to conflicts.

**Example Usage**
```bash
# Update the shell to the latest version
sudo aursh-update

# Check if an update is available without installing
aursh-update check

# Switch the update channel
aursh-update change beta
```

**How it works internally**
It runs a standalone binary (to allow elevated execution without elevating the entire shell). It resolves the installation directory, executes `git stash --quiet` to safely hide local modifications, and pulls the newest commits. Finally, it triggers a rebuild using `make` or `.NET SDK` to compile the fresh code.

---

## Standard POSIX Built-ins

**What it does**
AurSh implements all the standard, expected shell built-ins natively so standard shell scripts work flawlessly out of the box.

**Example Usage**
```bash
# Changing directories
cd /var/log

# Managing variables
export MY_VAR="hello"
unset MY_VAR

# Sourcing scripts
source ./env.sh
```

**How it works internally**
These commands are handled by the `BuiltinCommands.cs` router. Instead of launching a new operating system process, they directly invoke C# methods that manipulate the `ShellEnvironment` dictionary (for variables like `export`), the `workingDirectory` reference (for `cd`), or process handles (for `jobs`, `fg`, and `kill`).

**Supported POSIX Built-ins:**
`cd`, `export`, `unset`, `exit`, `history`, `clear`, `echo`, `pwd`, `type`, `alias`, `unalias`, `source`, `set`, `env`, `true`, `false`, `shift`, `read`, `test`, `return`, `jobs`, `fg`, `kill`.
