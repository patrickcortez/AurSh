# GRM (Git Repo Manager)

**What it does**
GRM is a built-in package manager for AurShell. Instead of downloading software from a centralized package repository, GRM searches and installs code directly from GitHub repositories.

**Why it's cool**
Because it uses GitHub directly, you get live, up-to-date results instantly with absolutely no local storage overhead.

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
  4. If a `.grm` script is found in the repository root, GRM prompts for your consent. If trusted (`[y]` or `[a]`), it securely executes the `[INSTALL]` section's post-install setup commands line-by-line.

### 3. `grm run <owner/repository> [--branch <branch>]`

Executes the `[RUN]` section of a repository's `.grm` script.

- **Example:** `grm run nushell/nushell --branch development`
- **Behavior:**
  1. Checks if the repository is already installed.
  2. Checks out the specified branch (defaults to `master` if not provided).
  3. Validates the `.grm` file formatting.
  4. Executes the `@start` to `@end` execution block located under the `[RUN]` section of the `.grm` file.

### 4. `grm list`

Lists all repositories currently installed and tracked by GRM.

- **Example:** `grm list`
- **Behavior:** Prints the local mapping of installed repositories and their absolute paths in the filesystem.

### 5. `grm goto <owner/repository>`

Changes your current working directory to the specified installed repository.

- **Example:** `grm goto nushell/nushell`
- **Behavior:** Searches `~/.grm/remotes.con` for `nushell/nushell`. If found, updates the shell's `PWD` and `OLDPWD`, and executes the underlying directory change.

### 6. `grm upgrade [owner/repository]`

Pulls the latest changes for a specific installed repository, or updates **all** installed repositories if run without arguments.

- **Example:** `grm upgrade nushell/nushell` (Specific) or `grm upgrade` (Global)
- **Behavior:**
  1. For a single repo: Checks if you have uncommitted or stashed changes. If clean, executes `git pull`. It will first perform a dry-run fetch to ensure the remote is still accessible.
  2. For a global upgrade: Automatically loops through all registered entries in `remotes.con` and sequentially runs the update process for each.

### 7. `grm info <owner/repository>`

Fetches detailed repository metadata and displays the project's README.

- **Example:** `grm info nushell/nushell`
- **Behavior:**
  1. Requests `https://api.github.com/repos/{owner}/{repo}` to retrieve the description, stars, forks, and license.
  2. Requests the raw README file using the `application/vnd.github.v3.raw` HTTP header (acting similarly to a `curl` request).
  3. Prints both the metadata and the markdown content to your terminal.

### 8. `grm uninstall <owner/repository>`

Uninstalls (deletes) a repository from your local machine.

- **Example:** `grm uninstall nushell/nushell`
- **Behavior:** Deletes the repository directory from `~/Repos/owner/repository` and removes its entry from `~/.grm/remotes.con`.

---

## API Limits

GRM uses the public GitHub API, which has a limit of 60 requests per hour for anonymous users.

**How to unlock 5,000 requests per hour:**
1. Generate a Personal Access Token (PAT) on GitHub.
2. Set it as an environment variable in your profile: `export GITHUB_TOKEN="ghp_xxxxxx"`
3. GRM will automatically find it and use it.

---

## The `.grm` Configuration File

Repositories can include a `.grm` file in their root directory to automatically execute post-install steps like compiling, moving files, or installing dependencies, as well as define runnable scripts.

GRM utilizes AurSh's native `ScriptRunner` to execute these files, which provides full support for variables, conditional logic (`if`, `while`, `for`), logical operators (`&&`, `||`), and I/O redirection.

### Syntax

The file is structured using sections, specifically `[INSTALL]` and `[RUN]`.

1. **Declaration Block**: Anything before the first section header is executed normally and is designed for setting up global variables.
2. **Sections**: Define distinct execution targets. `grm install` executes the `[INSTALL]` section, while `grm run` executes the `[RUN]` section.
3. **Execution Block**: Inside a section, anything between `@start` and `@end` is executed under strict rules. If any command in this block exits with a non-zero code, the execution is immediately halted to prevent unintended side effects.

### Example

```bash
# Global variables declared here are available in all sections
BuildDir="build"
Target="release"

[INSTALL]
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

[RUN]
@start
echo "Starting application..."
./bin/app --release
@end
```

---

## Architecture and Storage

- **Configuration Path:** `~/.grm/remotes.con`
  - This file tracks all installed packages in a simple `"remote-name"="path-to-repo"` format.
- **Installation Path:** `~/Repos/`
  - All cloned repositories are stored here by default.
- **Network Strategy:** GRM uses `HttpClient` to communicate with the `api.github.com/search/repositories` endpoint and uses `ProcessStartInfo` to execute local `git` commands (`clone`, `fetch`, `pull`).
