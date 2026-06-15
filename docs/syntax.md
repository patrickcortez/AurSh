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
AurSh supports POSIX-style logic blocks like `if/elif/else`, `for`, and `while` loops.

**Example Usage**
```bash
# Condition Evaluation
if [ -f "config.json" ]; then
    echo "Config found"
fi

# For Loop
for file in *.txt; do
    echo "Processing $file"
done

# While Loop
while [ $count -lt 10 ]; do
    echo $count
done
```

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
