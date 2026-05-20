# AutoSuggest

**AurSh's** *Auto-Suggest* is very much extensible using json files.
You can simply drop a *json* file of the app,package or tool you want to make auto suggestions,
in the folder: `.aursh/suggestions`. It's structure is relatively simple, as seen in the example
down below:

```json

{
  "command": "git",
  "subcommands": [
    "add",
    "bisect",
    "blame",
    "branch",
    "checkout",
    "cherry-pick",
    "clean",
    "clone",
    "commit",
    "config",
    "describe",
    "diff",
    "fetch",
    "format-patch",
    "gc",
    "grep",
    "init",
    "log",
    "merge",
    "mv",
    "notes",
    "pull",
    "push",
    "rebase",
    "reflog",
    "remote",
    "reset",
    "restore",
    "revert",
    "rm",
    "shortlog",
    "show",
    "stash",
    "status",
    "submodule",
    "switch",
    "tag",
    "worktree"
  ],
  "flags": [
    "--help",
    "--version",
    "--verbose",
    "--quiet",
    "--no-pager",
    "--git-dir",
    "--work-tree"
  ]
}

```
