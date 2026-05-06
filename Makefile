# AurShell Makefile
# Cross-platform build, publish, and install automation
# Works on: Linux, macOS, Windows (via MSYS2/Git-Bash), Termux

SHELL := /bin/bash
PROJECT := src/AurShell.csproj
BIN_DIR := bin
APP_NAME := aursh
VERSION := 0.1.0

# ──────────────────────────────────────────────
# OS and Architecture Detection
# ──────────────────────────────────────────────

UNAME_S := $(shell uname -s 2>/dev/null || echo Windows)
UNAME_M := $(shell uname -m 2>/dev/null || echo x86_64)

ifeq ($(findstring MINGW,$(UNAME_S)),MINGW)
    DETECTED_OS := Windows
else ifeq ($(findstring MSYS,$(UNAME_S)),MSYS)
    DETECTED_OS := Windows
else ifeq ($(findstring CYGWIN,$(UNAME_S)),CYGWIN)
    DETECTED_OS := Windows
else ifeq ($(UNAME_S),Windows)
    DETECTED_OS := Windows
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

info:
	@echo "OS:          $(DETECTED_OS)"
	@echo "Arch:        $(ARCH)"
	@echo "RID:         $(RID)"
	@echo "Executable:  $(EXE)"
	@echo "Install Dir: $(INSTALL_DIR)"
	@echo "User Dir:    $(USER_INSTALL_DIR)"

# ──────────────────────────────────────────────
# Build Targets
# ──────────────────────────────────────────────

build:
	@echo "[build] Compiling debug build..."
	dotnet build $(PROJECT) -c Debug
	@echo "[build] Output: $(BIN_DIR)/$(EXE)"

release:
	@echo "[release] Compiling release build..."
	dotnet build $(PROJECT) -c Release
	@echo "[release] Output: $(BIN_DIR)/$(EXE)"

publish:
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

# ──────────────────────────────────────────────
# Install Targets
# ──────────────────────────────────────────────

install: publish
ifeq ($(DETECTED_OS),Windows)
	@echo "[install] Installing to $(INSTALL_DIR)..."
	@mkdir -p "$(INSTALL_DIR)" 2>/dev/null || powershell -Command "New-Item -Path '$(INSTALL_DIR)' -ItemType Directory -Force" 2>/dev/null || true
	@cp "$(PUBLISH_DIR)/$(EXE)" "$(INSTALL_DIR)/$(EXE)" 2>/dev/null || \
		powershell -Command "Copy-Item '$(PUBLISH_DIR)/$(EXE)' '$(INSTALL_DIR)/$(EXE)' -Force"
	@echo "[install] Installed to $(INSTALL_DIR)/$(EXE)"
	@echo ""
	@echo "  Add to PATH if not already present:"
	@echo "  [System.Environment]::SetEnvironmentVariable('PATH', "
	@echo "    [System.Environment]::GetEnvironmentVariable('PATH', 'User') + ';$(INSTALL_DIR)', 'User')"
	@echo ""
else
	@echo "[install] Installing to $(INSTALL_DIR)/$(EXE)..."
	install -d "$(INSTALL_DIR)"
	install -m 755 "$(PUBLISH_DIR)/$(EXE)" "$(INSTALL_DIR)/$(EXE)"
	@echo "[install] Installed to $(INSTALL_DIR)/$(EXE)"
	@echo "[install] Run 'aursh' to start."
endif

install-user: publish
	@echo "[install] Installing to $(USER_INSTALL_DIR)/$(EXE)..."
ifeq ($(DETECTED_OS),Windows)
	@mkdir -p "$(USER_INSTALL_DIR)" 2>/dev/null || powershell -Command "New-Item -Path '$(USER_INSTALL_DIR)' -ItemType Directory -Force" 2>/dev/null || true
	@cp "$(PUBLISH_DIR)/$(EXE)" "$(USER_INSTALL_DIR)/$(EXE)" 2>/dev/null || \
		powershell -Command "Copy-Item '$(PUBLISH_DIR)/$(EXE)' '$(USER_INSTALL_DIR)/$(EXE)' -Force"
	@echo ""
	@echo "  Add to PATH if not already present:"
	@echo "  [System.Environment]::SetEnvironmentVariable('PATH', "
	@echo "    [System.Environment]::GetEnvironmentVariable('PATH', 'User') + ';$(USER_INSTALL_DIR)', 'User')"
	@echo ""
else
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
endif
	@echo "[install] Done. Run 'aursh' to start."

uninstall:
	@echo "[uninstall] Removing aursh..."
ifeq ($(DETECTED_OS),Windows)
	@rm -f "$(INSTALL_DIR)/$(EXE)" 2>/dev/null || \
		powershell -Command "Remove-Item '$(INSTALL_DIR)/$(EXE)' -Force -ErrorAction SilentlyContinue" 2>/dev/null || true
	@rm -f "$(USER_INSTALL_DIR)/$(EXE)" 2>/dev/null || \
		powershell -Command "Remove-Item '$(USER_INSTALL_DIR)/$(EXE)' -Force -ErrorAction SilentlyContinue" 2>/dev/null || true
else
	rm -f "$(INSTALL_DIR)/$(EXE)"
	rm -f "$(USER_INSTALL_DIR)/$(EXE)"
endif
	@echo "[uninstall] Done."

# ──────────────────────────────────────────────
# Utility Targets
# ──────────────────────────────────────────────

run: build
	@echo "[run] Launching aursh..."
ifeq ($(DETECTED_OS),Windows)
	@cd $(BIN_DIR) && ./$(EXE)
else
	@cd $(BIN_DIR) && ./$(EXE)
endif

clean:
	@echo "[clean] Removing build artifacts..."
	dotnet clean $(PROJECT) -c Debug --nologo -v q 2>/dev/null || true
	dotnet clean $(PROJECT) -c Release --nologo -v q 2>/dev/null || true
	rm -rf $(BIN_DIR)/* 2>/dev/null || true
	rm -rf publish 2>/dev/null || true
	rm -rf src/obj 2>/dev/null || true
	rm -rf src/bin 2>/dev/null || true
	@echo "[clean] Done."
