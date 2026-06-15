# AurShell Syntax Guide

**What it does**
AurShell is a cross-platform shell with a syntax designed to feel instantly familiar to users of `bash` or `zsh`. It provides built-in support for pipelines, I/O redirection, variable expansions, loops, conditional statements, and custom `.aur` shell scripts.

**Example Usage**
```bash
# Variables and logic
if [ "$USER" == "admin" ]; then
    echo "Welcome back, Admin!"
else
    echo "Access Denied"
fi

# Pipelining
cat output.log | grep "Error" > errors.txt
```

**How it works internally**
1. When you enter a command, AurSh's internal `Parser` reads the raw string and breaks it down into individual tokens (words, pipes, strings).
2. It expands any `$VARIABLES` or `~` home directory paths.
3. If it detects control flow (`if`, `for`, `while`), it passes execution to the `ScriptRunner` which manages scoping and block logic.
4. It finally hands the commands to the `Executor` to spin up native processes or internal built-ins, wiring up standard input/output through the pipeline operators.

---

## Script Execution

**What it does**
AurSh can execute batch script files with the `.aur` extension.

**Example Usage**
```bash
# Invoke AurSh manually
aursh my_script.aur arg1

# Or associate the extension and run it directly
aursh-assoc .aur aursh
./my_script.aur arg1
```

## Basic Commands and Execution

**What it does**
You can chain multiple commands together using standard operators.

**Example Usage**
*   **Pipes (`|`)**: Passes the output of one command as input to the next. For Windows users running PowerShell commands, object pipelines are perfectly preserved natively without the need to double-escape strings!
    ```bash
    # Standard byte-stream piping
    cat file.txt | grep "search"
    
    # Native PowerShell object piping (Double wrap your strings)
    Get-ChildItem | Where-Object {$_.Extension -eq "'.txt'"}
    ```
*   **Logical AND (`&&`)**: Executes the second command only if the first succeeds.
    ```bash
    make build && ./run
    ```
*   **Logical OR (`||`)**: Executes the second command only if the first fails.
    ```bash
    cat file.txt || echo "File not found"
    ```
*   **Sequential (`;`)**: Executes commands sequentially.
    ```bash
    echo "First"; echo "Second"
    ```

## Variables, Arrays and Expansion

**What it does**
You can store and retrieve data in variables, use indexed and associative arrays, and access special shell states (like exit codes).

**Example Usage**
```bash
# Accessing basic variables
echo "My home is $HOME"

# Special Variables
echo "Last exit code: $?"
echo "Script name: $0"
echo "First argument: $1"

# Advanced Expansion
# Use default value 'production' if ENV is not set
echo "Running in ${ENV:-production} mode"

# Indexed Arrays
my_array=("apple" "banana" "cherry")
echo "First item: ${my_array[0]}"

# Associative Arrays (Dictionaries)
declare -A my_dict
my_dict["name"]="AurSh"
echo "Name is ${my_dict["name"]}"
```

## Interactive Multi-line Commands

**What it does**
When using the interactive shell prompt, AurSh automatically detects if you've opened a block (like an `if` statement, `while` loop, `for` loop, or unclosed quote) and elegantly drops you into a multi-line continuation prompt (`>`) until the block is successfully closed.

**Example Usage**
```bash
$ for i in 1 2 3
> do
>   echo "Number $i"
> done
Number 1
Number 2
Number 3
$ 
```

**How it works internally**
The `InputHandler` inspects your current line before execution. If it sees keywords like `if`, `while`, `for`, or unclosed brackets/quotes, it buffers your input instead of executing it. Once the terminal detects `fi`, `done`, or matching quotes, it consolidates the buffer and securely executes the whole block through the internal `ScriptRunner`.

## Control Flow (Scripting)

**What it does**
AurSh supports POSIX-style logic blocks like `if/elif/else`, `case` statements, `for`, `while`, and `until` loops. You can also manipulate loop execution dynamically using `break` and `continue`.

**Example Usage**
```bash
# Condition Evaluation
if [ -f "config.json" ]; then
    echo "Config found"
fi

# Case Statements
case "$1" in
    start)
        echo "Starting service..."
        ;;
    stop)
        echo "Stopping service..."
        ;;
    *)
        echo "Unknown command"
        ;;
esac

# For Loop with continue
for file in *.txt; do
    if [ "$file" == "ignore.txt" ]; then
        continue
    fi
    echo "Processing $file"
done

# Until Loop with break
count=0
until [ $count -ge 10 ]; do
    if [ $count -eq 5 ]; then
        break
    fi
    echo $count
    count=$((count + 1))
done
```

## Math Evaluation

**What it does**
AurSh natively evaluates arithmetic expressions using double parentheses `$(( ))`. It supports basic operations (`+`, `-`, `*`, `/`, `%`) and honors standard mathematical operator precedence.

**Example Usage**
```bash
# Simple Arithmetic
result=$(( 5 + 10 * 2 ))
echo "Result is $result"

# Variable Incrementing
x=5
x=$(( x + 1 ))
```

**How it works internally**
The shell intercepts `$(( ))` syntax during parsing and forwards the raw expression to the internal `MathEvaluator.cs`, which processes standard mathematical operations seamlessly in C# without shelling out to external utilities like `expr`.

## Functions and Scoping

**What it does**
You can define reusable blocks of code using functions. Functions accept POSIX positional arguments dynamically and support safe, local variable scoping to avoid polluting the global environment. The `return` command can cleanly halt function execution and assign a return code.

**Example Usage**
```bash
# Define a function
function greet() {
    # 'local' ensures the variable disappears when the function ends
    local name=$1
    if [ -z "$name" ]; then
        echo "Error: Name required"
        return 1
    fi
    echo "Hello, $name! You passed $# arguments."
    return 0
}

# Call the function
greet "AurSh User"
```

**How it works internally**
When the `ScriptRunner` encounters a function definition, it pre-scans and saves the block into a dictionary without executing it. When you call the function, the runner pushes a new scope via `_env.PushScope()`, assigns `$1`, `$2`, `$@` and `$#` based on the passed arguments, executes the function body, handles any `_returnRequested` signals via the `return` builtin, and finally cleans up by calling `_env.PopScope()`.

## File Associations

**What it does**
AurShell natively handles cross-platform file extension associations via the `aursh-assoc` builtin. This allows you to execute script files directly (like `./script.py`) without needing to type `python` first or relying on OS-level handlers.

**Example Usage**
```bash
# Add an association (Use {0} for the file path, {1} for arguments)
aursh-assoc .py "python \"{0}\" {1}"

# Execute directly
./script.py arg1
```

**How it works internally**
When you invoke a file that has a registered association, AurShell intercepts the execution, replaces `{0}` with the script's path and `{1}` with the supplied arguments, and runs the substituted command behind the scenes.
