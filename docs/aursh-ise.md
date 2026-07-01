# AurSh-ISE

**What it does**
AurSh-ISE (Integrated Scripting Environment) is a graphical code editor specifically tailored for writing, editing, and managing `.aur` shell scripts. 

Built natively with Avalonia UI and AvaloniaEdit, it provides a cross-platform desktop interface equipped with rich syntax highlighting custom-built for the AurShell grammar.

**Key Features**
- **Native Syntax Highlighting**: Fully understands AurSh's unique syntax, perfectly highlighting modern scripting keywords (`const`, `function`, `try`, `import`), variables (`$VAR`, `${VAR}`), strings, and AurSh-specific native commands.
- **Cross-Platform**: Because it is built with Avalonia, it runs seamlessly on Windows, macOS, and Linux alongside your shell.
- **Dedicated Environment**: Provides an out-of-the-box editing experience without requiring external extensions for VS Code or other text editors.

**How it works internally**
The ISE leverages the `AurSh.xshd` XML syntax definition file loaded by the AvaloniaEdit component to dynamically tokenize and colorize code in real time as you type.

### Example Usage
To launch the ISE (if built and present in your path or current directory):
```bash
# Open a script directly in the editor
aursh-ise ./my_script.aur
```
