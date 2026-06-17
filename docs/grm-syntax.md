# The `.grm` Script Syntax Guide

**What it does**
`.grm` files allow developers to script automatic installation steps (like running `make` or moving files) when someone downloads their project through GRM.

Because GRM uses AurSh's internal engine, you can use standard bash syntax (like variables and if-statements) inside your `.grm` files!

---

## File Structure

A `.grm` file is split into a global declaration block followed by distinct execution sections:

### 1. The Global Setup Block
Anything **before** the first `[SECTION]` header is global setup.
- Use it to define variables like `$Target` or `$Version`.
- If a command fails here, the script will *keep going*.

### 2. The Sections
Sections target specific GRM commands:
- `[INSTALL]`: Runs when a user types `grm install`
- `[RUN]`: Runs when a user types `grm run`

### 3. The Strict Execution Block (`@start` to `@end`)
Inside a section, the actual commands go between `@start` and `@end`.
- If **any** command inside this block fails (exits with a non-zero code), the entire installation is instantly canceled to prevent damage.

### Example Skeleton
```bash
# -- GLOBAL DECLARATION BLOCK --
BuildDir="build"
Target="release"

[INSTALL]
@start
# -- INSTALL EXECUTION BLOCK --
mkdir $BuildDir
cd $BuildDir
make $Target
sudo make install
@end

[RUN]
@start
# -- RUN EXECUTION BLOCK --
echo "Starting application..."
./bin/app --release
@end
```

---

## Syntax Features (Inherited from AurSh)

Because `.grm` scripts are evaluated by AurSh's `ScriptRunner`, you have access to standard shell mechanics.

### Variables
You can define and expand variables using standard shell syntax.
```bash
Directory="/opt/myapp"
Version="1.0.0"

[INSTALL]
@start
echo "Installing version $Version to $Directory"
@end
```

### Logical Operators
You can chain commands using `&&` (AND) and `||` (OR).
```bash
[INSTALL]
@start
# Only runs make install if make succeeds
make && sudo make install

# Prints an error if cd fails
cd my_dir || echo "Failed to change directory!"
@end
```

### Control Flow
`.grm` files fully support `if`, `for`, `while`, and `until` loops.

#### If / Elif / Else
```bash
[INSTALL]
@start
if [ -d "build" ]; then
    echo "Build directory exists!"
elif [ -f "Makefile" ]; then
    echo "Running make..."
    make
else
    echo "Nothing to do."
fi
@end
```

#### For Loops
```bash
[INSTALL]
@start
for file in src/*.cs; do
    echo "Compiling $file"
done
@end
```

#### While Loops
```bash
[RUN]
@start
while [ -f "lock.tmp" ]; do
    echo "Waiting for lock..."
    sleep 1
done
@end
```

### I/O Redirection and Pipes
Standard Unix-style piping (`|`) and redirection (`>`, `>>`) are natively supported.
```bash
[INSTALL]
@start
# Redirect output to a log file
make > build.log

# Pipe output through grep
ls -la | grep ".cs"
@end
```

### Functions
You can define reusable functions within the declaration block and call them in the execution block.
```bash
function cleanup() {
    rm -rf temp/
}

[INSTALL]
@start
echo "Building..."
make
cleanup
@end
```

---

## Best Practices
1. **Always use the Declaration Block for setup**: Keep the `@start` / `@end` block focused strictly on commands that *must* succeed.
2. **Handle errors gracefully**: If a command is expected to fail but shouldn't halt the installation, use `|| true` to force a `0` exit code.
   ```bash
   [INSTALL]
   @start
   # If rm fails (e.g. file doesn't exist), the script continues
   rm old_config.json || true 
   @end
   ```
3. **Keep it portable**: Rely on cross-platform commands where possible, or use control flow to detect the OS before running platform-specific commands.
