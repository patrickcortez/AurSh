# AurSh SSH Manager (`aursh-ssh`)

---

## What it does

The `aursh-ssh` tool provides a beautiful, interactive Terminal User Interface (TUI) for managing your SSH connections and keys. 

Instead of typing long `ssh user@hostname -p port -i key` commands or manually editing your `~/.ssh/config` file, this built-in tool lets you securely save, manage, and launch your remote connections directly from a visual list.

---

## Key Features

- **Visual Host List**: Save your frequently accessed remote servers and connect to them by simply selecting them from a list.
- **Key Management**: View all of your generated SSH keys (like `ed25519` or `rsa`), check their fingerprints, and generate new ones easily.
- **Interactive Prompts**: Prompts you for names, users, IPs, and ports dynamically without leaving the interface.

---

## Example Usage

To open the SSH manager, simply type the command into your terminal:

```bash
aursh-ssh
```

Once the interface opens, you can use the following keyboard shortcuts to navigate and manage your items:

| Action | Keybinding | Description |
| :--- | :--- | :--- |
| **Navigate** | `Up` / `Down` | Move your selection up or down the list. |
| **Switch Tabs** | `Left` / `Right` | Switch between your "SSH-Keys" list and your "Hosts" list. |
| **Connect** | `Enter` | (On a Host) Instantly opens a secure SSH connection to the selected server. |
| **Add New** | `Ctrl + N` | Create a new Host or generate a new SSH Key depending on your current tab. |
| **Edit** | `Ctrl + E` | Edit the details of a saved Host. |
| **Delete** | `Ctrl + D` | Delete a saved Host or safely remove an SSH Key. |
| **Exit** | `Ctrl + C` | Close the SSH manager and return to the normal shell prompt. |

---

## How it works internally

### 1. Boot-time Check
When AurSh boots up, it silently checks your system's `PATH` to verify that standard `ssh` and `ssh-keygen` binaries are installed. If they are missing, `aursh-ssh` will immediately inform you that SSH is not available, avoiding unexpected crashes.

### 2. TUI Rendering (Alternate Buffer)
When you type `aursh-ssh`, the shell switches to an "alternate screen buffer" (using standard terminal escape codes). This pauses your standard shell prompt and draws the visual interface. When you exit, the alternate buffer is closed, and your terminal looks exactly as it did before you opened the tool.

### 3. Data Storage
- **Hosts**: The servers you save are stored locally in a simple, fast JSON file located at `~/.aursh/ssh/hosts.json`. AurSh uses Native AOT-safe JSON serialization, which means it reads and writes this data incredibly fast without bloating the shell's memory.
- **Keys**: Your keys are managed directly from your `~/.ssh/` directory. When you generate a new key using `Ctrl+N`, AurSh passes your input over to the native `ssh-keygen` tool in the background to handle the heavy cryptography safely.

### 4. Handoff to Native SSH
When you select a host and press `Enter`, AurSh temporarily steps aside. It drops out of the TUI mode and directly invokes the native `ssh` command with your saved parameters. The native SSH client takes full control of the terminal for your remote session. Once you disconnect from the remote server, AurSh takes control back and returns you to your local prompt.
