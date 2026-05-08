# AurSh


![badge](https://shields.io/badge/Aursh-lightblue) ![badge2](https://shields.io/badge/Ver_0.1-pink) ![badge3](https://shields.io/badge/Cross_Platform-lightgreen)

A cross compatible shell developed in C# by Tezzz, is a system shell
similar to most shells like bash, zsh and fish.

This is basically a rewrite of Linuxify, my old project. But cross compatible between different Operating Systems: e.g. Linux, Windows, MacOS and Termux(Android).

It has a two line prompt with modern looking UI like PowerLevel10k thats verbose by nature and it has its own plug-in sytem using lua scripts to allow you to extend the shell with custom behavior. The plugin system has passive and active plugins allowing you to make new commands or create new behaviors for the shell.

It also has file associations, so you can associate file extensions with their respective compiler or interpreter.

Make sure you use the font *JetBrainsMono Nerd Font* installed for the prompt to look the way it good.

Has:
- Shell Scripting: .aur scripts
- A rc script: .aurc
- Piping and redirection
- ghost text and auto suggestions
- Persistent History
- Environmental variable handling.
- resolves commands to native OS Commands.
- Job control.
- Plug-in system using lua (aursh-plugin <add,list,del,init>)
- File Association (e.g: aursh-assoc .py "python", then: ./script.py arg...)


---

## Built-ins

- aursh-plugin <add,list,del,init,debug> : plugin system of the shell 
- aursh-assoc <extension> <command> : file association
- aursh-reload : reloads shell
- aursh-history <clear,show,filter=<pattern>> : TUI history with query abilities
- aursh-about : basic info about AurSh


---

## Preview

**Windows**

![Windows](/Assets/Images/Windows.png)

**Linux**

![Linux](/Assets/Images/Linux.png)

**Android**

![Android](/Assets/Images/Android.jpg)

**MacOS**

> (Image Unavailable because I dont own a Mac)

---

## Icon

![icon](/Assets/Images/aura-icon.png)

---

## Installation

Make sure you have *make* and the *.NET SDK* installed.

Once you have them installed simply run `make install-user` for current user installation and `make install` for system wide installation.

---

## Uninstall

To uninstall you can simply run `make uninstall`.

---

## LICENSE

This project is under GNU General Public License, see license file for more information.