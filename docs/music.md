# AurSh Music

**What it does**
AurSh Music is a built-in local music player that streams your own audio files directly through a beautiful web interface. It runs completely offline and requires no external subscriptions.

**Why it's cool**
It features a full React-based UI that looks and feels like modern streaming apps, complete with playlist management, shuffle/repeat, and custom album art extraction!

---

## Setup & Usage

### 1. Set your Music Folder

First, tell AurSh where you keep your `.mp3` or `.m4a` files. Run this command in the shell:

```bash
aursh-music set-dir /path/to/your/music/folder
```

### 2. Start the Server

Once configured, simply launch the music server:

```bash
aursh-music
```

### 3. Open the Player

Open your favorite web browser and navigate to:
**[http://127.0.0.1:7007](http://127.0.0.1:7007)**

---

## Features

- **Live Scanning**: The server will automatically scan your folder for music files.
- **Album Art**: It extracts ID3 metadata to display beautiful cover art.
- **Playlists**: You can create custom playlists and add/remove songs dynamically.
- **Liked Songs**: Save your favorite tracks into a dedicated "Liked Songs" view.
- **Background Play**: Because it runs in your browser, you can close the terminal window (if running as a background service) and keep listening!
