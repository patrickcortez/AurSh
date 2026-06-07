# AurSh Net Module

## What it does

`aursh-net` is a built-in networking utility for AurSh. 

It allows you to:
- Scan and connect to Wi-Fi networks using an interactive menu.
- Send and receive files across your local network securely.
- Manage allowed IP addresses for incoming file transfers.

---

## Network Management

Manage your Wi-Fi connections easily from the terminal.

**Scan and select a network interactively:**
```bash
aursh-net list
```

**Connect manually to a Wi-Fi network:**
```bash
aursh-net connect "MyHomeWiFi" "MyPassword123"
```

**Disconnect from the current network:**
```bash
aursh-net disconnect
```

**View your network information and IP address:**
```bash
aursh-net info
```

---

## File Transfers

Seamlessly send files or directories to other devices running AurSh on your local network.

**Discover nearby peers and send a file interactively:**
```bash
aursh-net send ./my_project
```

**Send a file directly to a specific IP address:**
```bash
aursh-net send ./my_project 192.168.1.50
```

---

## Security (IP Whitelisting)

To ensure secure file transfers, you can manage which IP addresses are allowed to send files to your device.

**Allow an IP address:**
```bash
aursh-net allow 192.168.1.50
```

**Remove an IP address from the allowed list:**
```bash
aursh-net disallow 192.168.1.50
```

**View all allowed IP addresses:**
```bash
aursh-net allowed
```

---

## How it works internally

1. **Wi-Fi Management**: 
   - `aursh-net` detects your operating system automatically. 
   - It runs native tools in the background (`netsh` for Windows, `nmcli` for Linux, or `networksetup` for macOS) to manage connections seamlessly.

2. **File Transfer Daemon**: 
   - Whenever AurSh starts, a background listener (daemon) opens on port `15333`.
   - This listener automatically handles incoming files.

3. **Receiving Files & Security**: 
   - When someone uses `aursh-net send` to your IP, the daemon receives the data.
   - It unpacks the files safely into the `~/Downloads/AurshNet/` directory.
   - *Security Check*: Incoming transfers are validated against your allowed IP list to prevent unauthorized access.
