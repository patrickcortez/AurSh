# Repository Structure

**What it does**
This repository organizes the AurSh codebase, built binaries, documentation, and graphical assets into a simple directory structure. AurSh itself is designed to act as a lightweight layer over your operating system rather than shipping a massive bundle of its own custom command-line utilities.

**Example Usage**
When navigating the repository, you'll find the following key directories:
- `src/` — Contains the C# source code for AurSh.
- `bin/` — Contains the compiled executable binaries.
- `docs/` — Contains the documentation you are reading now.
- `Assets/` — Contains fonts, icons, and images used by the project.
- `AurSh-ISE/` — Contains the Integrated Scripting Environment editor for AurSh.

**How it works internally**
1. **The Core**: AurSh intercepts your commands, but instead of reinventing the wheel, it hands off standard commands directly to your underlying Operating System (Windows, macOS, or Linux).
2. **The Plugin Layer**: Before passing commands to the OS, AurSh checks your `.aurc` configuration file and your loaded plugins (written in Lua, F#, or compiled binaries). 
3. **Execution**: If a plugin or alias matches your command, AurSh handles it internally. If not, it safely passes the execution down to the OS (`Plugins & .aurc -> AurSh -> OS`).
