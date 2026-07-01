# AurShell Syntax Guide

**What it does**
AurShell is a fast, cross-platform shell. Its syntax is designed to feel instantly familiar to users of `bash` or `zsh`, while introducing modern, JavaScript-like features for advanced scripting. 

It supports pipelines, variables, loops, object-oriented syntax, and custom `.aur` shell scripts natively.

### Example
```bash
# Basic Variables and logic
if [ "$USER" == "admin" ]; then
    echo "Welcome back, Admin!"
else
    echo "Access Denied"
fi

# Modern Data Structures
let config = { "theme": "dark", "version": 2 }
echo "Theme: ${config.theme}"
```

### How it works internally
1. **Lexer**: Reads your command and breaks it into tokens.
2. **Parser**: Builds an Abstract Syntax Tree (AST) for commands, expressions, and blocks.
3. **Linter**: Scans for common syntax errors early.
4. **Evaluator**: Executes the tree, managing variables and OS processes natively.

---

## Script Execution

**What it does**
AurSh executes batch script files with the `.aur` extension natively.

**Example Usage**
```bash
# Invoke AurSh manually
aursh my_script.aur arg1

# Or run directly if associated
./my_script.aur arg1
```

---

## Basic Commands and Execution

**What it does**
Chain multiple commands together using standard POSIX operators.

**Example Usage**
*   **Pipes (`|`)**: Passes output to the next command. Preserves PowerShell objects natively on Windows.
    ```bash
    cat file.txt | grep "search"
    ```
*   **Logical AND (`&&`) / OR (`||`)**: Conditional execution based on success or failure.
    ```bash
    make build && ./run
    cat file.txt || echo "File not found"
    ```
*   **Sequential (`;`)**: Executes sequentially.
    ```bash
    echo "First"; echo "Second"
    ```

---

## Variables, Arrays, and Objects

**What it does**
Store and retrieve data using `let` and `const`. AurShell natively supports JSON-like arrays and objects, making it incredibly powerful for structured data.

**Example Usage**
```bash
# Basic assignment
const PI = 3.14
let version = $(git --version)
echo "I am running $version"

# Indexed Arrays
let my_array = ["apple", "banana", "cherry"]
echo "First item: ${my_array[0]}"

# Array Methods
my_array.push("date")
let total = my_array.length()

# Objects (Dictionaries)
let my_dict = { "name": "AurSh", "version": "1.0" }
echo "Name is ${my_dict.name}"

# Property Assignment
my_dict.author = "Cortez"
```

**How it works internally**
The `AurValueParser` securely evaluates JSON-like syntax during assignment. Variables are stored as rich types (`AurInt`, `AurString`, `AurList`, `AurObject`), allowing you to call methods natively on them.

---

## Modern Expressions and Math

**What it does**
Evaluate arithmetic and logical expressions natively without external utilities. Use `let` or `const` for direct assignments.

**Example Usage**
```bash
# Direct Evaluation
let x = 5
let result = x + 10 * 2

# Logical Comparisons
let is_valid = (x > 0 && result == 25)
```

**How it works internally**
The `ExpressionParser` processes mathematical and logical operators using an Abstract Syntax Tree (AST) directly in C#, honoring standard operator precedence.

---

## Control Flow (Scripting)

**What it does**
AurSh supports POSIX-style logic blocks (`if/elif/else`, `case`, `for`, `while`) and modern flow controls like `break` and `continue`.

**Example Usage**
```bash
# Condition Evaluation
if [ "$count" -eq 0 ]; then
    echo "Count is zero"
fi

# For Loop with continue
for file in *.txt; do
    if [ "$file" == "ignore.txt" ]; then
        continue
    fi
    echo "Processing $file"
done
```

---

## Error Handling (Try / Catch)

**What it does**
Modern exception handling directly in the shell using `try`, `catch`, and `throw`.

**Example Usage**
```bash
try {
    if [ ! -f "config.json" ]; then
        throw "Configuration file is missing!"
    fi
    echo "Config loaded."
} catch e {
    echo "Error caught: $e"
}
```

**How it works internally**
The evaluator wraps the try block execution. If a `throw` command or internal exception occurs, execution safely jumps to the catch block and assigns the error to the catch variable.

---

## Modules (Import and Export)

**What it does**
AurShell supports modular scripting! `export` variables or functions from one file and `import` them into another as an object.

**Example Usage**
```bash
# In utils.aur
export function greet(name) {
    echo "Hello, $name"
}
export const AppName = "AurSh"

# In main.aur
let utils = import "utils.aur"
utils.greet("User")
echo "App: ${utils.AppName}"
```

**How it works internally**
The `import` command runs the target script in an isolated environment. `export` commands register values, which are then returned as a rich `AurObject` to the caller.

---

## Command Substitution

**What it does**
Execute a command in the background and capture its text output, or capture its exit code directly.

**Example Usage**
```bash
# Capture stdout into a variable
current_dir=$(pwd)
echo "I am currently in $current_dir"

# Inline Exit Code Evaluation ($?)
# Suppresses stdout and stderr, returning the integer exit code directly
let success = $?(git push origin main)
if [ $success -eq 0 ]; then
    echo "Pushed successfully!"
fi
```

---

## Process Substitution

**What it does**
Treat the output of a command like a temporary file.

**Example Usage**
```bash
diff <(ls folderA) <(ls folderB)
```

**How it works internally**
Executes the inner command asynchronously and connects its output to an anonymous Named Pipe.

---

## Here Strings and Here Docs

**What it does**
Pass strings or multi-line text directly into standard input without temporary files.

**Example Usage**
```bash
# Here String
grep "admin" <<< "user1 admin user2"

# Here Doc
cat <<EOF > config.txt
Server=127.0.0.1
EOF
```

---

## Functions and Scoping

**What it does**
Define reusable blocks of code. Functions accept arguments dynamically and support safe local variable scoping.

**Example Usage**
```bash
function greet() {
    local name=$1
    echo "Hello, $name! You passed $# arguments."
    return 0
}

# Call the function
greet "AurSh User"
```

**How it works internally**
When called, the evaluator pushes a new Call Stack Frame, assigns `$1`, `$2`, executes the function, handles `return` signals, and pops the scope.

---

## File Associations

**What it does**
Native cross-platform file extension associations via `aursh-assoc`.

**Example Usage**
```bash
aursh-assoc .py "python \"{0}\" {1}"
./script.py arg1
```
