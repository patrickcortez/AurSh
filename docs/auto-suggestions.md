# Auto-Suggestions

**What it does**
AurSh provides "ghost text" auto-suggestions as you type, helping you autocomplete commands, subcommands, and flags quickly. It is highly extensible, allowing you to add custom completions for any app or tool by simply creating a JSON file.

**Example Usage**
To add auto-suggestions for a tool, create a `.json` file inside the `.aursh/suggestions/` directory.

Here is an example for the `git` command:

```json
{
  "command": "git",
  "subcommands": [
    "add",
    "commit",
    "push",
    "pull",
    "status"
  ],
  "flags": [
    "--help",
    "--version",
    "--verbose"
  ]
}
```

**How it works internally**
1. When AurSh starts, it scans the `.aursh/suggestions/` folder and loads all the JSON configuration files into memory.
2. As you type a command in the terminal, AurSh's input handler looks at the first word (the command) and checks if there is a matching configuration.
3. If it finds a match, it cross-references what you are typing against the available `subcommands` and `flags`.
4. The closest match is rendered on your screen as dimmed "ghost text" ahead of your cursor, which you can accept by pressing the right arrow key or Tab.
