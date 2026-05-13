<h2 align="center">AurSh</h2>

<h3 align="center"> <i> A cross platform shell to make the command line aesthetically pleasing</i> </h3>

<p align="center">
<img src= "https://shields.io/badge/Aursh-lightblue"> <img src="https://shields.io/badge/Ver_1.5-pink"> <img src="https://shields.io/badge/Cross_Platform-lightgreen" <img src="https://shields.io/badge/BlackBox-black">>
</p>

<p align="center">
 <img height="256" width="256" alt="image" src="https://github.com/patrickcortez/AurSh/blob/master/Assets/Images/aura-icon.png" />
</p>
<div align="center">
A cross compatible shell developed in C# by Tezzz, is a system shell
similar to most shells like bash, zsh and fish.

This is basically a rewrite of Linuxify, my old project. But cross compatible between different Operating Systems: e.g. Linux, Windows, MacOS and Termux(Android).

It has a two line prompt with modern looking UI like PowerLevel10k thats verbose by nature and it has its own plug-in sytem using lua or F# scripts to allow you to extend the shell with custom behavior. The plugin system has passive and active plugins allowing you to make new commands or create new behaviors for the shell.

It also has file associations, so you can associate file extensions with their respective compiler or interpreter.

Make sure you use the font *JetBrainsMono Nerd Font* installed for the prompt to look the way it good. 

When using the shell, make sure you put any process/app that you have installed that takes over the terminal in the bypass list: `~/.aursh/bypass.txt`. 

</div>

Has:
- Shell Scripting: .aur scripts
- A rc script: .aurc
- Piping and redirection
- ghost text and auto suggestions
- Persistent History
- Environmental variable handling.
- resolves commands to native OS Commands.
- Job control.
- Plug-in system using lua or F# (aursh-plugin <add,list,del,init>)
- File Association (e.g: aursh-assoc .py "python", then: ./script.py arg...)
- BlackBox: TUI execution viewport that wraps every command's stdin/stdout/stderr in a rounded Unicode box drawn beneath the prompt (in progress, see [docs/blackbox.md](docs/blackbox.md)).
- Updater: A tool to update the shell from the remote repository.

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

---

## Preview

**Windows**

![Windows](/Assets/Images/Windows3.png)

**Linux**

![Linux](/Assets/Images/Linux3.png)

**Android**

![Android](/Assets/Images/Screenshot_2026-05-09-21-57-45-17_84d3000e3f4017145260f7618db1d683.jpg)

**MacOS**

> (Image Unavailable because I dont own a Mac)

---

## Installation

Make sure you have *.NET SDK* installed, *make* is optional since there is a MSBuild alternative.

Once you have them installed simply run `make install-user` for current user installation and `make install` for system wide installation or you can use .Net's *MSBuild* to install the shell: `dotnet msbuild build.proj -t:Install` for system wide installation or `dotnet msbuild build.proj -t:InstallUser` for current user installation.

```bash
dotnet msbuild build.proj -t:Install
```

---

## Uninstall

To uninstall you can simply run `make uninstall`.

---

## LICENSE

This project is under GNU General Public License, see license file for more information.
