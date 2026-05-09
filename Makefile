# AurShell Makefile
# Cross-platform build, publish, and install automation
# Works on: Linux, macOS, Windows (native + MSYS2/Git-Bash), Termux

PROJECT := src/AurShell.csproj
BIN_DIR := bin
APP_NAME := aursh
VERSION := 1.3.0

# ──────────────────────────────────────────────
# OS and Architecture Detection
# ──────────────────────────────────────────────

UNAME_S := $(shell uname -s 2>nul || echo Windows)
UNAME_M := $(shell uname -m 2>nul || echo x86_64)

ifeq ($(findstring MINGW,$(UNAME_S)),MINGW)
    DETECTED_OS := Windows
    WIN_ENV := msys
else ifeq ($(findstring MSYS,$(UNAME_S)),MSYS)
    DETECTED_OS := Windows
    WIN_ENV := msys
else ifeq ($(findstring CYGWIN,$(UNAME_S)),CYGWIN)
    DETECTED_OS := Windows
    WIN_ENV := msys
else ifeq ($(UNAME_S),Windows)
    DETECTED_OS := Windows
    WIN_ENV := native
else ifeq ($(UNAME_S),Darwin)
    DETECTED_OS := macOS
else ifeq ($(UNAME_S),Linux)
    ifneq ($(wildcard /data/data/com.termux),)
        DETECTED_OS := Termux
    else ifdef ANDROID_ROOT
        DETECTED_OS := Termux
    else
        DETECTED_OS := Linux
    endif
else
    DETECTED_OS := Linux
endif

ifeq ($(findstring arm,$(UNAME_M)),arm)
    ARCH := arm64
else ifeq ($(findstring aarch64,$(UNAME_M)),aarch64)
    ARCH := arm64
else
    ARCH := x64
endif

# ──────────────────────────────────────────────
# Shell Selection
# ──────────────────────────────────────────────
# GNU Make 3.81 on Windows uses cmd.exe by default.
# We keep cmd.exe as the shell and wrap Windows commands
# in explicit powershell calls. MSYS/Git-Bash uses bash.

ifneq ($(WIN_ENV),native)
    SHELL := /bin/bash
endif

# PowerShell invocation prefix for native Windows
PS := powershell -NoProfile -NoLogo -Command

# ──────────────────────────────────────────────
# Runtime Identifier and Platform Paths
# ──────────────────────────────────────────────

ifeq ($(DETECTED_OS),Windows)
    RID := win-$(ARCH)
    EXE := $(APP_NAME).exe
    INSTALL_DIR := C:/Program Files/AurShell
    USER_INSTALL_DIR := $(subst \,/,$(LOCALAPPDATA))/AurShell
    ifeq ($(USER_INSTALL_DIR),/AurShell)
        USER_INSTALL_DIR := $(subst \,/,$(USERPROFILE))/AppData/Local/AurShell
    endif
    PUBLISH_DIR := publish/$(RID)
    PATHSEP := ;
else ifeq ($(DETECTED_OS),macOS)
    RID := osx-$(ARCH)
    EXE := $(APP_NAME)
    INSTALL_DIR := /usr/local/bin
    USER_INSTALL_DIR := $(HOME)/.local/bin
    PUBLISH_DIR := publish/$(RID)
    PATHSEP := :
else ifeq ($(DETECTED_OS),Termux)
    RID := linux-$(ARCH)
    EXE := $(APP_NAME)
    INSTALL_DIR := $(PREFIX)/bin
    USER_INSTALL_DIR := $(HOME)/.local/bin
    PUBLISH_DIR := publish/$(RID)
    PATHSEP := :
else
    RID := linux-$(ARCH)
    EXE := $(APP_NAME)
    INSTALL_DIR := /usr/local/bin
    USER_INSTALL_DIR := $(HOME)/.local/bin
    PUBLISH_DIR := publish/$(RID)
    PATHSEP := :
endif

# ──────────────────────────────────────────────
# Targets
# ──────────────────────────────────────────────

.PHONY: all build release publish install install-user uninstall clean run info help

all: build

help:
ifeq ($(WIN_ENV),native)
	@cmd /c "echo."
	@echo   AurShell v$(VERSION) - Build System
	@echo   -------------------------------------
	@cmd /c "echo."
	@echo   make build          Debug build
	@echo   make release        Release build
	@echo   make publish        Self-contained single-file release
	@echo   make install        Publish + install to system dir (needs admin)
	@echo   make install-user   Publish + install to user dir (no admin)
	@echo   make uninstall      Remove installed binary
	@echo   make clean          Remove all build artifacts
	@echo   make run            Build and launch interactive shell
	@echo   make info           Show detected platform info
	@echo   make help           Show this help
	@cmd /c "echo."
	@echo   Detected: $(DETECTED_OS) $(ARCH) [$(RID)]
	@echo   Install:  $(INSTALL_DIR)
	@cmd /c "echo."
else
	@echo ""
	@echo "  AurShell v$(VERSION) — Build System"
	@echo "  ─────────────────────────────────────"
	@echo ""
	@echo "  make build          Debug build (framework-dependent)"
	@echo "  make release        Release build (framework-dependent)"
	@echo "  make publish        Self-contained single-file release"
	@echo "  make install        Publish + install to system directory (may need sudo)"
	@echo "  make install-user   Publish + install to user directory (no sudo)"
	@echo "  make uninstall      Remove from system directory"
	@echo "  make clean          Remove all build artifacts"
	@echo "  make run            Build and launch interactive shell"
	@echo "  make info           Show detected platform info"
	@echo "  make help           Show this help"
	@echo ""
	@echo "  Detected: $(DETECTED_OS) $(ARCH) [$(RID)]"
	@echo "  Install:  $(INSTALL_DIR)"
	@echo ""
endif

info:
ifeq ($(WIN_ENV),native)
	@echo OS:          $(DETECTED_OS)
	@echo Arch:        $(ARCH)
	@echo RID:         $(RID)
	@echo Executable:  $(EXE)
	@echo Install Dir: $(INSTALL_DIR)
	@echo User Dir:    $(USER_INSTALL_DIR)
	@echo Win Env:     $(WIN_ENV)
else
	@echo "OS:          $(DETECTED_OS)"
	@echo "Arch:        $(ARCH)"
	@echo "RID:         $(RID)"
	@echo "Executable:  $(EXE)"
	@echo "Install Dir: $(INSTALL_DIR)"
	@echo "User Dir:    $(USER_INSTALL_DIR)"
endif

# ──────────────────────────────────────────────
# Build Targets
# ──────────────────────────────────────────────

build:
ifeq ($(WIN_ENV),native)
	@echo [build] Compiling debug build...
	dotnet build $(PROJECT) -c Debug
	@echo [build] Output: $(BIN_DIR)/$(EXE)
else
	@echo "[build] Compiling debug build..."
	dotnet build $(PROJECT) -c Debug
	@echo "[build] Output: $(BIN_DIR)/$(EXE)"
endif

release:
ifeq ($(WIN_ENV),native)
	@echo [release] Compiling release build...
	dotnet build $(PROJECT) -c Release
	@echo [release] Output: $(BIN_DIR)/$(EXE)
else
	@echo "[release] Compiling release build..."
	dotnet build $(PROJECT) -c Release
	@echo "[release] Output: $(BIN_DIR)/$(EXE)"
endif

publish:
ifeq ($(WIN_ENV),native)
	@echo [publish] Publishing self-contained $(RID) binary...
	dotnet publish $(PROJECT) -c Release -r $(RID) --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:IncludeNativeLibrariesForSelfExtract=true -o $(PUBLISH_DIR)
	@echo [publish] Output: $(PUBLISH_DIR)/$(EXE)
else
	@echo "[publish] Publishing self-contained $(RID) binary..."
	dotnet publish $(PROJECT) \
		-c Release \
		-r $(RID) \
		--self-contained true \
		-p:PublishSingleFile=true \
		-p:PublishTrimmed=true \
		-p:IncludeNativeLibrariesForSelfExtract=true \
		-o $(PUBLISH_DIR)
	@echo "[publish] Output: $(PUBLISH_DIR)/$(EXE)"
endif

# ──────────────────────────────────────────────
# Install Targets
# ──────────────────────────────────────────────

install: publish
ifeq ($(WIN_ENV),native)
	@echo [install] Installing to $(INSTALL_DIR)...
	@$(PS) "New-Item -Path '$(INSTALL_DIR)' -ItemType Directory -Force | Out-Null"
	@$(PS) "Copy-Item -Path '$(PUBLISH_DIR)/$(EXE)' -Destination '$(INSTALL_DIR)/$(EXE)' -Force"
	@echo [install] Installed to $(INSTALL_DIR)/$(EXE)
	@cmd /c "echo."
	@$(PS) "$$p = [System.Environment]::GetEnvironmentVariable('PATH','Machine'); if ($$p -notlike '*$(INSTALL_DIR)*') { [System.Environment]::SetEnvironmentVariable('PATH', $$p + ';$(INSTALL_DIR)', 'Machine'); Write-Host '  PATH updated (Machine). Restart your terminal.' } else { Write-Host '  $(INSTALL_DIR) is already in PATH.' }"
	@cmd /c "echo."
else ifeq ($(DETECTED_OS),Windows)
	@echo "[install] Installing to $(INSTALL_DIR)..."
	mkdir -p "$(INSTALL_DIR)"
	cp "$(PUBLISH_DIR)/$(EXE)" "$(INSTALL_DIR)/$(EXE)"
	@echo "[install] Installed to $(INSTALL_DIR)/$(EXE)"
	@echo ""
	@powershell.exe -NoProfile -Command "$$p = [System.Environment]::GetEnvironmentVariable('PATH','Machine'); if ($$p -notlike '*$(INSTALL_DIR)*') { [System.Environment]::SetEnvironmentVariable('PATH', $$p + ';$(INSTALL_DIR)', 'Machine'); Write-Host '  PATH updated (Machine). Restart your terminal.' } else { Write-Host '  $(INSTALL_DIR) is already in PATH.' }"
	@echo ""
else
	@echo "[install] Installing to $(INSTALL_DIR)/$(EXE)..."
	install -d "$(INSTALL_DIR)"
	install -m 755 "$(PUBLISH_DIR)/$(EXE)" "$(INSTALL_DIR)/$(EXE)"
	@if [ -w "$(PREFIX)/etc/shells" ] && ! grep -Fxq "$(INSTALL_DIR)/$(EXE)" "$(PREFIX)/etc/shells"; then \
		echo "$(INSTALL_DIR)/$(EXE)" >> "$(PREFIX)/etc/shells"; \
		echo "[install] Added $(INSTALL_DIR)/$(EXE) to $(PREFIX)/etc/shells"; \
	elif [ -w /etc/shells ] && ! grep -Fxq "$(INSTALL_DIR)/$(EXE)" /etc/shells; then \
		echo "$(INSTALL_DIR)/$(EXE)" >> /etc/shells; \
		echo "[install] Added $(INSTALL_DIR)/$(EXE) to /etc/shells"; \
	fi
	@echo "[install] Installed to $(INSTALL_DIR)/$(EXE)"
	@echo "[install] Run 'aursh' to start."
endif

install-user: publish
ifeq ($(WIN_ENV),native)
	@echo [install] Installing to $(USER_INSTALL_DIR)/$(EXE)...
	@$(PS) "New-Item -Path '$(USER_INSTALL_DIR)' -ItemType Directory -Force | Out-Null"
	@$(PS) "Copy-Item -Path '$(PUBLISH_DIR)/$(EXE)' -Destination '$(USER_INSTALL_DIR)/$(EXE)' -Force"
	@echo [install] Installed to $(USER_INSTALL_DIR)/$(EXE)
	@cmd /c "echo."
	@$(PS) "$$p = [System.Environment]::GetEnvironmentVariable('PATH','User'); if ($$p -notlike '*$(USER_INSTALL_DIR)*') { [System.Environment]::SetEnvironmentVariable('PATH', $$p + ';$(USER_INSTALL_DIR)', 'User'); Write-Host '  PATH updated. Restart your terminal to use aursh.' } else { Write-Host '  $(USER_INSTALL_DIR) is already in PATH.' }"
	@cmd /c "echo."
	@echo [install] Done. Run 'aursh' to start.
else ifeq ($(DETECTED_OS),Windows)
	@echo "[install] Installing to $(USER_INSTALL_DIR)/$(EXE)..."
	mkdir -p "$(USER_INSTALL_DIR)"
	cp "$(PUBLISH_DIR)/$(EXE)" "$(USER_INSTALL_DIR)/$(EXE)"
	@echo "[install] Installed to $(USER_INSTALL_DIR)/$(EXE)"
	@echo ""
	@powershell.exe -NoProfile -Command "$$p = [System.Environment]::GetEnvironmentVariable('PATH','User'); if ($$p -notlike '*$(USER_INSTALL_DIR)*') { [System.Environment]::SetEnvironmentVariable('PATH', $$p + ';$(USER_INSTALL_DIR)', 'User'); Write-Host '  PATH updated (User). Restart your terminal.' } else { Write-Host '  $(USER_INSTALL_DIR) is already in PATH.' }"
	@echo ""
	@echo "[install] Done. Run 'aursh' to start."
else
	@echo "[install] Installing to $(USER_INSTALL_DIR)/$(EXE)..."
	mkdir -p "$(USER_INSTALL_DIR)"
	cp "$(PUBLISH_DIR)/$(EXE)" "$(USER_INSTALL_DIR)/$(EXE)"
	chmod +x "$(USER_INSTALL_DIR)/$(EXE)"
	@echo ""
	@if echo "$$PATH" | grep -q "$(USER_INSTALL_DIR)"; then \
		echo "[install] $(USER_INSTALL_DIR) is already in PATH."; \
	else \
		echo "[install] Add to PATH:"; \
		echo "  export PATH=\"$(USER_INSTALL_DIR):\$$PATH\""; \
		echo "  (Add this to your ~/.bashrc or ~/.zshrc)"; \
	fi
	@echo ""
	@echo "[install] Done. Run 'aursh' to start."
endif

uninstall:
ifeq ($(WIN_ENV),native)
	@echo [uninstall] Removing aursh...
	@$(PS) "if (Test-Path '$(INSTALL_DIR)/$(EXE)') { Remove-Item '$(INSTALL_DIR)/$(EXE)' -Force }"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(EXE)') { Remove-Item '$(USER_INSTALL_DIR)/$(EXE)' -Force }"
	@echo [uninstall] Done.
else ifeq ($(DETECTED_OS),Windows)
	@echo "[uninstall] Removing aursh..."
	rm -f "$(INSTALL_DIR)/$(EXE)"
	rm -f "$(USER_INSTALL_DIR)/$(EXE)"
	@echo "[uninstall] Done."
else
	@echo "[uninstall] Removing aursh..."
	rm -f "$(INSTALL_DIR)/$(EXE)"
	rm -f "$(USER_INSTALL_DIR)/$(EXE)"
	@echo "[uninstall] Done."
endif

# ──────────────────────────────────────────────
# Utility Targets
# ──────────────────────────────────────────────

run: build
ifeq ($(WIN_ENV),native)
	@echo [run] Launching aursh...
	@$(PS) "& './$(BIN_DIR)/$(EXE)'"
else
	@echo "[run] Launching aursh..."
	@cd $(BIN_DIR) && ./$(EXE)
endif

clean:
ifeq ($(WIN_ENV),native)
	@echo [clean] Removing build artifacts...
	-@dotnet clean $(PROJECT) -c Debug --nologo -v q 2>nul
	-@dotnet clean $(PROJECT) -c Release --nologo -v q 2>nul
	@$(PS) "if (Test-Path '$(BIN_DIR)') { Remove-Item '$(BIN_DIR)/*' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'publish') { Remove-Item 'publish' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'src/obj') { Remove-Item 'src/obj' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'src/bin') { Remove-Item 'src/bin' -Recurse -Force -ErrorAction SilentlyContinue }"
	@echo [clean] Done.
else
	@echo "[clean] Removing build artifacts..."
	dotnet clean $(PROJECT) -c Debug --nologo -v q 2>/dev/null || true
	dotnet clean $(PROJECT) -c Release --nologo -v q 2>/dev/null || true
	rm -rf $(BIN_DIR)/* 2>/dev/null || true
	rm -rf publish 2>/dev/null || true
	rm -rf src/obj 2>/dev/null || true
	rm -rf src/bin 2>/dev/null || true
	@echo "[clean] Done."
endif
