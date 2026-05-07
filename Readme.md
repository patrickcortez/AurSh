# AurSh

A cross compatible shell developed in C# by Tezzz, is a system shell
similar to most shells like bash, zsh and fish.

This is basically a rewrite of Linuxify, my old project. But cross compatible between different Operating Systems: e.g. Linux, Windows, MacOS and Termux(Android).

It has a two line prompt with modern looking UI like PowerLevel10k thats verbose by nature and it has its own plug-in sytem using lua to allow you to extend the shell with custom features.

Make sure you have the font JetBrainsMono Nerd Font installed for the prompt to look the way it good.

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

## Preview

**Windows**

![Windows](/Assets/Images/Windows.png)

**Linux**

![Linux](/Assets/Images/Linux.png)

**Android**

![Android](/Assets/Images/Android.jpg)

---

## Installation

Make sure you have make and the .NET SDK installed.
Once you have them installed simply run `make install-user` for non admin previlege and `make install` for system dir.

---

## Uninstall

To uninstall you can simply run `make uninstall`.

---

## LICENSE

This project is under GNU General Public License, see license file for more information.