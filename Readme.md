<h2 align="center">AurSh</h2>

<h3 align="center"> <i> A cross platform shell to make the command line aesthetically pleasing</i> </h3>

<p align="center">
<img src= "https://shields.io/badge/Aursh-lightblue"> <img src="https://shields.io/badge/Ver_2.0-pink"> <img src="https://shields.io/badge/Cross_Platform-lightgreen"> <img src="https://shields.io/badge/BlackBox-black">
</p>

<p align="center">
 <img height="256" width="256" alt="image" src="https://github.com/patrickcortez/AurSh/blob/master/Assets/Images/aura-icon.png" />
</p>
A cross compatible shell developed in C# by Tezzz, is a system shell
similar to most shells like bash, zsh and fish.

This is basically a rewrite of Linuxify, my old project. But cross compatible between different Operating Systems: e.g. Linux, Windows, MacOS and Termux(Android).

It has a two line prompt with modern looking UI like PowerLevel10k thats verbose by nature and it has its own plug-in sytem using lua or F# scripts to allow you to extend the shell with custom behavior. The plugin system has passive and active plugins allowing you to make new commands,modify the UI or create new behaviors for the shell.

It also has file associations, so you can associate file extensions with their respective compiler or interpreter.
The shell also has extensible auto-suggestions. Along with that is its own contexts which are disked back object like
data structure that can hold multiple attributes that each contain a value that is envokable in the command line for structuring data.

Make sure you use the font *JetBrainsMono Nerd Font* installed for the prompt to look the way it good.

When using the shell, make sure you put any process/app that you have installed that takes over the terminal in the bypass list: `~/.aursh/bypass.txt`.

</div>

Has:
- Shell Scripting: .aur scripts
- A rc script: .aurc
- Text and object Piping and redirection
- Ghost text and auto suggestions
- Persistent History
- Environmental variable handling.
- resolves commands to native OS Commands.
- Job control.
- Plug-in system using lua or F# (aursh-plugin <add,list,del,init>)
- File Associations (e.g: aursh-assoc .py "python", then: ./script.py arg...)
- `BlackBox`: TUI execution viewport that displays processes invoked from the command line inside a unicode box with
round edges.
- `Updater`: A tool to update the shell from the remote repository.
- `Contexts`: A disk backed object like that can hold multiple attributes to structurize and organize variables.

---

## Built-ins

- `aursh-plugin` <add,list,del,init,debug> : plugin system of the shell
- `aursh-assoc` <extension> <command> : file association
- `aursh-reload` : reloads shell
- `aursh-history` <clear,show,filter=<pattern>> : TUI history with query abilities
- `aursh-about` : basic info about AurSh
- `aursh-ls` : A TUI file system explorer.
- `aursh-cat <options: -e> <file>` : A pipable file reader and a *vim-like* TUI text editor ( with `-e` flag ).
- `aursh-update` : updates the shell from the remote repository then exits the shell to apply changes.
- `aursh-context` : A disk backed object like that can hold multiple attributes to structurize and organize variables. `aursh-context <new,del,list,insert,remove,update> <args...>`

---

## Preview

**Windows**

![Windows](/Assets/Images/Windows4.png)

**Linux**

![Linux](/Assets/Images/Linux4.png)

**Android**

![Android](/Assets/Images/Android1.png)

**MacOS**

> (Image Unavailable because I dont own a Mac)

---

## Structure

This repositories structure is relatively simple:

- **Assets** : Contains fonts and images
- **src** : Contains all the source code
- **docs** : Contains documentations
- **scripts** : Contains Helper scripts

---

## Installation

Clone this repository first then make sure you have *.NET SDK* installed and *make* is optional since there is a *MSBuild* alternative.

Once you have them installed simply run `make install-user` for current user installation and `make install` for system wide installation or you can use .Net's *MSBuild* to install the shell: `dotnet msbuild build.proj -t:Install` for system wide installation or `dotnet msbuild build.proj -t:InstallUser` for current user installation.

### Android

On *Android* make sure you have `Termux` installed and inside *Termux*,
Make sure you have proot installed: `pkg install proot`and `pkg install proot-distro`.

Log-in To your *proot* with `proot-distro login <distro>`.

After than you may proceed to install the *.Net SDK* and *make*.

Then run `make install`.


---

## Uninstall

To uninstall you can simply run `make uninstall`.

---

## LICENSE

This project is under GNU General Public License, see [license](/LICENSE) file for more information.
