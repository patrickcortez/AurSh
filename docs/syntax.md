# AurShell Syntax Guide

AurShell is designed with a syntax that will feel familiar to users of `bash`, `zsh`, and other POSIX-compliant shells. It supports pipelines, redirections, variable expansions, control structures, and scripting capabilities.

## Basic Commands and Execution
Commands are parsed sequentially. You can chain commands using various operators:

*   **Pipes (`|`)**: Passes the output of one command as input to the next.
    ```bash
    cat file.txt | grep "search"
    ```
*   **Logical AND (`&&`)**: Executes the second command only if the first succeeds (exit code 0).
    ```bash
    make build && ./run
    ```
*   **Logical OR (`||`)**: Executes the second command only if the first fails (exit code non-zero).
    ```bash
    cat file.txt || echo "File not found"
    ```
*   **Sequential (`;` or newline)**: Executes commands sequentially.
    ```bash
    echo "First"; echo "Second"
    ```
*   **Background (`&`)**: Executes the command in the background.
    ```bash
    long_process &
    ```

## Redirection
AurShell supports standard I/O redirections:

*   `>`: Redirect standard output to a file (overwrite).
*   `>>`: Redirect standard output to a file (append).
*   `<`: Redirect standard input from a file.
*   `2>`: Redirect standard error to a file.
*   `2>>`: Redirect standard error to a file (append).
*   `2>&1`: Redirect standard error to standard output.

Example:
```bash
build_project > output.log 2>&1
```

## Quoting and Escaping
*   **Single Quotes (`'...'`)**: Preserves the literal value of all characters. No variable expansion occurs.
*   **Double Quotes (`"..."`)**: Preserves the literal value of all characters, *except* for `$`, which allows for variable expansion.
*   **Escape Character (`\`)**: Escapes the next character (e.g., `\n` for newline, `\t` for tab, or to escape quotes).

## Variables and Expansion

You can expand variables using `$` or `${}` syntax.

### Special Variables
*   `$?`: The exit code of the last executed command.
*   `$$`: The process ID (PID) of the current shell.
*   `$0`: The name of the script.
*   `$1`, `$2`, ...: Positional parameters passed to the script or function.
*   `$@`, `$*`: All positional parameters.
*   `$#`: The number of positional parameters.
*   `~`: Expands to the user's home directory.

### Parameter Expansion
AurShell supports advanced parameter expansion:
*   `${VAR:-default}`: If `VAR` is unset or empty, substitute `default`.
*   `${VAR:=default}`: If `VAR` is unset or empty, substitute `default` AND assign it to `VAR`.
*   `${VAR:+alt}`: If `VAR` is set and not empty, substitute `alt`.
*   `${VAR:?error}`: If `VAR` is unset or empty, print `error` and exit.

### Object Access
AurShell allows access to specific properties of shell objects:
```bash
echo ${obj.field}
```

## Control Flow (Scripting)

AurShell supports rich control flow for `.aur` scripts.

### If / Elif / Else
```bash
if [ -f "config.json" ]; then
    echo "Config found"
elif [ -d "config" ]; then
    echo "Config directory found"
else
    echo "No config found"
fi
```

### For Loops
```bash
for file in *.txt; do
    echo "Processing $file"
done
```

### While and Until Loops
```bash
while [ $count -lt 10 ]; do
    echo $count
done

until [ -f "ready.txt" ]; do
    echo "Waiting..."
done
```

### Condition Evaluation (`[...]` or `[[...]]`)
AurShell can evaluate conditions using brackets.

**File and String Operators:**
*   `-z string`: True if string is empty.
*   `-n string`: True if string is not empty.
*   `-f file`: True if it is a regular file.
*   `-d dir`: True if it is a directory.
*   `-e path`: True if the file or directory exists.
*   `-r file`: True if file is readable.
*   `-w file`: True if file is writable.
*   `-x file`: True if file is executable.
*   `-s file`: True if file exists and its size is greater than zero.

**Comparison Operators:**
*   `=` or `==`: String equality.
*   `!=`: String inequality.
*   `-eq`, `-ne`: Numeric equality / inequality.
*   `-lt`, `-le`: Numeric less than / less than or equal to.
*   `-gt`, `-ge`: Numeric greater than / greater than or equal to.
*   `!`: Negate the condition.

## Functions
You can define functions using the `function` keyword or the `()` syntax.

```bash
function my_func {
    echo "Argument 1: $1"
    return 0
}

# OR

my_func() {
    echo "Argument 1: $1"
    return 0
}
```
Functions have their own scope for positional parameters (`$1`, `$2`, etc.). You can use `return` to exit a function with a specific exit code.

## File Associations

AurShell natively handles cross-platform file extension associations via the `aursh-assoc` builtin. This allows you to execute script files directly (like `./script.py`) without prepending the interpreter.

### Managing Associations
```bash
# List all associations
aursh-assoc

# Add or update an association
# Note: Use {0} as a placeholder for the file path, and {1} for remaining arguments.
aursh-assoc .py "python \"{0}\" {1}"
aursh-assoc .js "node \"{0}\" {1}"

# Remove an association
aursh-assoc --remove .js
```

### Execution Behavior
When you invoke a file that has a registered association (e.g. `./main.py arg1`), AurShell intercepts the execution, replaces `{0}` with the script's path and `{1}` with `arg1`, and runs the substituted command. This completely avoids OS-specific association handlers (like `ftype` on Windows) and shebangs.
