# AurSh Internals and Structure

**What it does**
AurSh is designed to be a modular, cross-platform shell. Under the hood, it is structured into several interconnected components that handle everything from parsing commands to drawing the beautiful TUI (Terminal User Interface).

This document explains the internal structure of the `src` directory and how the different pieces of AurSh work together.

---

## High-Level Architecture

At its core, AurSh operates in a continuous read-eval-print loop (REPL). It reads text, parses it into an actionable command, executes it, and then redraws the prompt.

```mermaid
graph TD;
    A[User Input] --> B[Parser];
    B --> C{Is Built-in?};
    C -- Yes --> D[Core/Builtins Router];
    C -- No --> E[System Process Execution];
    D --> F[Shell Environment Update];
    E --> G[BlackBox Execution Viewport];
    F --> H[Graphics/Prompt Redraw];
    G --> H;
```

---

## Directory Structure Breakdown

The codebase is split into distinct modules to keep things organized. Here is a simplified explanation of what each part does:

### 1. `core/` and `Program.cs`
This is the heart of the shell. It manages the main execution loop, environmental variables, and the `ShellEnvironment` state. `Program.cs` serves as the entry point that spins up the shell process.

### 2. `Parser/`
Before any command runs, it has to be understood. The parser takes raw text (like `echo "hello" | grep h`) and breaks it down into tokens. It handles pipes, quotes, file associations, and command substitution.

### 3. `graphics/`
AurSh is built to be visually pleasing. This folder contains all the logic for drawing the modern two-line prompt, handling ANSI escape sequences, colors, and building visual elements for the TUI native tools (like `aursh-ls` and `aursh-cat`).

### 4. `blackbox/`
When you execute a standard operating system command (like `ping` or `npm install`), AurSh can encapsulate the output inside a beautiful Unicode box with rounded edges. The `blackbox` module intercepts the standard output of these child processes and draws the box around them in real-time.

### 5. `Contexts/`
Contexts are disk-backed, object-like variables unique to AurSh. This module handles saving and retrieving these variables from the `.aursh/` configuration directory, allowing you to persist complex data structures across different shell sessions.

### 6. `lua/` and `plugins/`
AurSh features an extensible plugin system. The `lua` module integrates a Lua interpreter directly into the shell, allowing users to write scripts that can interact with the shell's internal C# API. The `plugins` module manages the loading and unloading of these scripts.

### 7. `aursh-update/`
A standalone mini-program included with the shell. It lives separately so you can run updates with elevated privileges (like `sudo aursh-update`) without needing to run your entire interactive shell as the root user.

### 8. `BuiltinCommands.cs`
The router for native commands. Instead of spawning new processes, it matches commands like `cd`, `export`, or `aursh-plugin` to native C# functions, executing them instantly.

---

## Summary

By keeping the visual elements (`graphics`, `blackbox`) cleanly separated from the logical processing (`Parser`, `core`), AurSh remains highly maintainable while providing a fast and dynamic user experience across Windows, Linux, Android, and macOS.
