# GPM (Git Package Manager)

**GPM** is a built-in package manager for AurShell that leverages the GitHub API and Git version control to search, install, upgrade, and manage repositories directly from the shell.

Unlike traditional package managers that maintain large offline indexes, GPM uses the GitHub REST API to perform live repository searches, ensuring you always get accurate and up-to-date results with zero local storage overhead.

---

## Commands

### 1. `gpm search <query>`
Searches GitHub for repositories matching the query.
- **Example:** `gpm search nushell`
- **Behavior:** Queries the GitHub API and returns the top 10 matching repositories (in `username/repo` format).

### 2. `gpm install <repository>`
Installs (clones) a repository to your local machine.
- **Example:** `gpm install nushell/nushell`
- **Behavior:** 
  - Clones the target repository into `~/Repos/<repo-name>`.
  - Registers the repository into GPM's tracking file at `~/.gpm/remotes.con`.

### 3. `gpm list`
Lists all repositories currently installed and tracked by GPM.
- **Example:** `gpm list`
- **Behavior:** Prints the local mapping of installed repositories and their absolute paths in the filesystem.

### 4. `gpm goto <repository>`
Immediately navigates the shell's working directory to the installed repository.
- **Example:** `gpm goto nushell`
- **Behavior:** Changes the shell's `PWD` (Present Working Directory) and the process environment directory to the location where the repository was cloned.

### 5. `gpm upgrade <repository>`
Upgrades an installed repository by fetching and pulling the latest changes from the remote.
- **Example:** `gpm upgrade nushell`
- **Behavior:** Navigates to the repository path and executes `git pull`. It will first perform a dry-run fetch to ensure the remote is still accessible.

### 6. `gpm info <repository>`
Fetches detailed repository information (description, stars, forks, license) directly from GitHub.
- **Example:** `gpm info nushell/nushell`
- **Behavior:** Makes an API request to `https://api.github.com/repos/{owner}/{repo}` to retrieve metadata.

### 7. `gpm uninstall <repository>`
Uninstalls (deletes) a repository from your local machine.
- **Example:** `gpm uninstall nushell`
- **Behavior:** Deletes the repository directory from `~/Repos/` and removes its entry from `~/.gpm/remotes.con`.

---

## Authentication and API Limits

GPM uses the GitHub REST API, which has a rate limit of 60 requests per hour for unauthenticated users. 

**To increase your limit to 5,000 requests per hour:**
1. Generate a Personal Access Token (PAT) on GitHub.
2. Set it as an environment variable in your system or profile named `GITHUB_TOKEN`.
   - E.g., `export GITHUB_TOKEN="ghp_xxxxxxxxxxxx"`
3. GPM will automatically read this token and attach it to API requests.

---

## Architecture and Storage

- **Configuration Path:** `~/.gpm/remotes.con`
  - This file tracks all installed packages in a simple `"remote-name"="path-to-repo"` format.
- **Installation Path:** `~/Repos/`
  - All cloned repositories are stored here by default.
- **Network Strategy:** GPM uses `HttpClient` to communicate with the `api.github.com/search/repositories` endpoint and uses `ProcessStartInfo` to execute local `git` commands (`clone`, `fetch`, `pull`).
