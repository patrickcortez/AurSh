# AurSh Net Module

`aursh-net` is a native, cross-platform networking utility built directly into AurSh. It allows you to manage Wi-Fi connections with an aesthetically pleasing Terminal User Interface (TUI) and send/receive files seamlessly across your local network without any third-party dependencies.

## Features

- **Wi-Fi Scanner**: List and select Wi-Fi networks around you using an interactive arrow-key menu.
- **Cross-Platform**: Works out-of-the-box on Windows (`netsh`), Linux (`nmcli`), and macOS (`airport`/`networksetup`).
- **Peer-to-Peer File Transfer**: Send and receive folders seamlessly via a background daemon.

## Commands

### `aursh-net`
Opens a general dashboard displaying current network status, signal strength, local IP address, and available commands.

### `aursh-net list`
Scans the surrounding area for Wi-Fi networks and launches an interactive TUI. Use the **Up/Down** arrow keys to navigate and **Enter** to select a network. You will be prompted to input a password if required.

### `aursh-net connect <ssid> [password]`
Manually connects to the given `ssid`. If the network requires a password, supply it as the second argument.

### `aursh-net disconnect`
Disconnects from the currently active Wi-Fi network.

### `aursh-net send <path-to-file/folder> <ip>`
Transfers a file or an entire directory recursively to the target IP address. The target machine must be running AurSh, as it relies on the built-in receiver daemon listening on port `15333`.

### `aursh-net info`
Displays colored information regarding your current connection status, including SSID, Signal Strength (Percentage and Bars), Local IP Address, and Receiver Daemon status.

## File Transfer Details
Whenever you start `aursh`, a background daemon is automatically spun up on port `15333`. If you receive a file via `aursh-net send`, the daemon will seamlessly unpack it into `~/Downloads/AurshNet/` while maintaining the directory structure.
