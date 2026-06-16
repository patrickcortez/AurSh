# GRM (Git Repo Manager)

**GRM** is a built-in package manager for AurShell that leverages the GitHub API and Git version control to search, install, upgrade, and manage repositories directly from the shell.

Unlike traditional package managers that maintain large offline indexes, GRM uses the GitHub REST API to perform live repository searches, ensuring you always get accurate and up-to-date results with zero local storage overhead.

---

## Commands

### 1. `grm search <query>`
Searches GitHub for repositories matching the query.
- **Example:** `grm search nushell`
- **Behavior:** Queries the GitHub API and returns the top 10 matching repositories (in `username/repo` format).

### 2. `grm install <owner/repository>`
Clones a GitHub repository locally into your `~/Repos/` directory and registers it.
- **Example:** `grm install nushell/nushell`
- **Behavior:**
  1. Checks if the repository is already installed.
  2. Runs `git clone https://github.com/nushell/nushell.git ~/Repos/nushell/nushell`.
  3. Saves the mapping `nushell/nushell -> ~/Repos/nushell/nushell` into `~/.grm/remotes.con`.

### 3. `grm list`
Lists all repositories currently installed and tracked by GRM.
- **Example:** `grm list`
- **Behavior:** Prints the local mapping of installed repositories and their absolute paths in the filesystem.

### 4. `grm goto <owner/repository>`
Changes your current working directory to the specified installed repository.
- **Example:** `grm goto nushell/nushell`
- **Behavior:** Searches `~/.grm/remotes.con` for `nushell/nushell`. If found, updates the shell's `PWD` and `OLDPWD`, and executes the underlying directory change.

### 5. `grm upgrade <owner/repository>`
Pulls the latest changes for a specific installed repository.
- **Example:** `grm upgrade nushell/nushell`
- **Behavior:** Checks if you have uncommitted or stashed changes. If the repository is clean, it navigates to the repository path and executes `git pull`. It will first perform a dry-run fetch to ensure the remote is still accessible.

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

## Architecture and Storage

- **Configuration Path:** `~/.grm/remotes.con`
  - This file tracks all installed packages in a simple `"remote-name"="path-to-repo"` format.
- **Installation Path:** `~/Repos/`
  - All cloned repositories are stored here by default.
- **Network Strategy:** GRM uses `HttpClient` to communicate with the `api.github.com/search/repositories` endpoint and uses `ProcessStartInfo` to execute local `git` commands (`clone`, `fetch`, `pull`).
