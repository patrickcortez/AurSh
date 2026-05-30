# AurSh Net Module

**What it does**
`aursh-net` is a built-in networking utility for AurSh. It lets you scan for and connect to Wi-Fi networks using a sleek, interactive terminal menu. It also lets you seamlessly send and receive files or entire folders to other devices on your local network without needing any third-party tools.

**Example Usage**
To open the interactive Wi-Fi scanner and select a network:
```bash
aursh-net list
```

To manually connect to a specific Wi-Fi network:
```bash
aursh-net connect "MyHomeWiFi" "MyPassword123"
```

To send a folder to another computer running AurSh:
```bash
aursh-net send ./my_project 192.168.1.50
```

To view your current connection status and IP address:
```bash
aursh-net info
```

**How it works internally**
1. **Wi-Fi Management**: `aursh-net` acts as a cross-platform wrapper. When you run a command, it detects your operating system and runs the appropriate native tool in the background (e.g. `netsh` on Windows, `nmcli` on Linux, or `networksetup` on macOS).
2. **File Transfer Daemon**: Every time you launch AurSh, it automatically starts a background listener (daemon) on port `15333`. 
3. **Receiving Files**: When someone uses `aursh-net send` to your IP address, this background daemon catches the incoming data and safely unpacks it into the `~/Downloads/AurshNet/` directory.
