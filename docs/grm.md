# GRM (Git Repo Manager)

**GRM** is a built-in repository manager for AurShell that leverages the GitHub API and Git version control to search, install, upgrade, and manage repositories directly from the shell.

Unlike traditional package managers that maintain large offline indexes, GRM uses the GitHub REST API to perform live repository searches, ensuring you always get accurate and up-to-date results with zero local storage overhead.

---

## Commands

### 1. `grm search <query>`

Searches GitHub for repositories matching the query.

- **Example:** `grm search nushell`
- **Behavior:** Queries the GitHub API and returns the top 10 matching repositories (in `username/repo` format).

### 2. `grm install <owner/repository> [--branch <branch>]`

Clones a GitHub repository locally into your `~/Repos/` directory and registers it.

- **Example:** `grm install nushell/nushell --branch development`
- **Behavior:**
  1. Checks if the repository is already installed.
  2. Runs `git clone` (optionally targeting a specific branch with `-b`).
  3. Saves the mapping `nushell/nushell -> ~/Repos/nushell/nushell` into `~/.grm/remotes.con`.
  4. If a `.grm` script is found in the repository root, GRM prompts for your consent. If trusted (`[y]` or `[a]`), it securely executes the post-install setup commands line-by-line using AurShell's native scripting engine, supporting piping (`|`), logic operators (`&&`), and control flows (`if`).

### 3. `grm list`

Lists all repositories currently installed and tracked by GRM.

- **Example:** `grm list`
- **Behavior:** Prints the local mapping of installed repositories and their absolute paths in the filesystem.

### 4. `grm goto <owner/repository>`

Changes your current working directory to the specified installed repository.

- **Example:** `grm goto nushell/nushell`
- **Behavior:** Searches `~/.grm/remotes.con` for `nushell/nushell`. If found, updates the shell's `PWD` and `OLDPWD`, and executes the underlying directory change.

### 5. `grm upgrade [owner/repository]`

Pulls the latest changes for a specific installed repository, or updates **all** installed repositories if run without arguments.

- **Example:** `grm upgrade nushell/nushell` (Specific) or `grm upgrade` (Global)
- **Behavior:**
  1. For a single repo: Checks if you have uncommitted or stashed changes. If clean, executes `git pull`. It will first perform a dry-run fetch to ensure the remote is still accessible.
  2. For a global upgrade: Automatically loops through all registered entries in `remotes.con` and sequentially runs the update process for each.

### 6. `grm info <owner/repository>`

Fetches detailed repository metadata and displays the project's README.

- **Example:** `grm info nushell/nushell`
- **Behavior:**
  1. Requests `https://api.github.com/repos/{owner}/{repo}` to retrieve the description, stars, forks, and license.
  2. Requests the raw README file using the `application/vnd.github.v3.raw` HTTP header (acting similarly to a `curl` request).
  3. Prints both the metadata and the markdown content to your terminal.

### 7. `grm uninstall <owner/repository>`

Uninstalls (deletes) a repository from your local machine.

- **Example:** `grm uninstall nushell/nushell`
- **Behavior:** Deletes the repository directory from `~/Repos/owner/repository` and removes its entry from `~/.grm/remotes.con`.

---

## Authentication and API Limits

GRM uses the GitHub REST API, which has a rate limit of 60 requests per hour for unauthenticated users.

**To increase your limit to 5,000 requests per hour:**

1. Generate a Personal Access Token (PAT) on GitHub.
2. Set it as an environment variable in your system or profile named `GITHUB_TOKEN`.
   - E.g., `export GITHUB_TOKEN="ghp_xxxxxxxxxxxx"`
3. GRM will automatically read this token and attach it to API requests.

---

## The `.grm` Configuration File

Repositories can include a `.grm` file in their root directory to automatically execute post-install steps like compiling, moving files, or installing dependencies.

GRM utilizes AurSh's native `ScriptRunner` to execute these files, which provides full support for variables, conditional logic (`if`, `while`, `for`), logical operators (`&&`, `||`), and I/O redirection.

### Syntax

The file is structured into two main parts:

1. **Declaration Block**: Anything before `@start` is executed normally and is designed for setting up variables.
2. **Execution Block**: Anything between `@start` and `@end` is executed under strict rules. If any command in this block exits with a non-zero code, the execution is immediately halted to prevent unintended side effects.

### Example

```bash
# Variables declared here are available in the execution block
BuildDir="build"
Target="release"

@start
# If the build directory exists, remove it
if [ -d $BuildDir ]; then
    rm -rf $BuildDir
fi

# Run the build process (execution halts if make fails)
make $Target

# Install the binary
sudo make install
@end
```

---

## Architecture and Storage

- **Configuration Path:** `~/.grm/remotes.con`
  - This file tracks all installed packages in a simple `"remote-name"="path-to-repo"` format.
- **Installation Path:** `~/Repos/`
  - All cloned repositories are stored here by default.
- **Network Strategy:** GRM uses `HttpClient` to communicate with the `api.github.com/search/repositories` endpoint and uses `ProcessStartInfo` to execute local `git` commands (`clone`, `fetch`, `pull`).
