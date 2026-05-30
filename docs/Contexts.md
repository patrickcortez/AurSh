# AurSh Contexts

**What it does**
A **Context** is a disk-backed, object-like data structure that holds multiple key-value attributes. It allows you to persistently store and easily retrieve grouped configuration data directly from your shell commands. 

**Example Usage**
You can create, modify, and delete Contexts using the `aursh-context` command.

To read a value from a Context directly in the command line, use the `<ContextName>:<AttributeName>` syntax:
```bash
# This prints the value of the 'User' attribute inside the 'Git' context
echo Git:User
```

**How it works internally**
1. When you use the `aursh-context` command, AurSh writes the key-value data to a file on your disk, ensuring it persists across shell sessions.
2. When you type a command containing `<ContextName>:<AttributeName>`, the shell's parser detects this pattern before executing the command.
3. The parser loads the corresponding Context file from disk, extracts the requested attribute's value, and replaces the text in your command with the actual value before it runs.
