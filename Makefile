# AurShell Makefile
# Cross-platform build, publish, and install automation
# Works on: Linux, macOS, Windows, Termux

PROJECT := src/AurShell.csproj
UPDATE_PROJECT := src/aursh-update/AurShUpdate.csproj
CONTEXT_PROJECT := src/Contexts/Contexts.csproj
BIN_DIR := bin
APP_NAME := aursh
UPDATE_APP_NAME := aursh-update
CONTEXT_APP_NAME := aursh-context
FONTS_DIR := Assets/fonts
FONT_FILE := JetBrainsMonoNLNerdFont-Light.ttf
VERSION := 3.0.0
ENSURE_DEPS := scripts/linux-termux-macos/ensure-deps.sh

# OS and Architecture Detection

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
    UNAME_O := $(shell uname -o 2>/dev/null || echo Linux)
    HAS_GLIBC := $(shell ldd --version 2>/dev/null | grep -i -E "glibc|gnu libc" >/dev/null 2>&1 && echo 1 || echo 0)
    ifeq ($(HAS_GLIBC),1)
        DETECTED_OS := Linux
    else ifneq ($(wildcard /data/data/com.termux),)
        DETECTED_OS := Termux
    else ifneq ($(findstring com.termux,$(PREFIX)),)
        DETECTED_OS := Termux
    else ifdef ANDROID_ROOT
        DETECTED_OS := Termux
    else ifeq ($(UNAME_O),Android)
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

# Shell Selection

ifneq ($(WIN_ENV),native)
    SHELL := /bin/bash
endif

# PowerShell invocation prefix for native Windows
PS := powershell -NoProfile -NoLogo -Command

# Runtime Identifier and Platform Paths

ifeq ($(DETECTED_OS),Windows)
    RID := win-$(ARCH)
    EXE := $(APP_NAME).exe
    UPDATE_EXE := $(UPDATE_APP_NAME).exe
    CONTEXT_EXE := $(CONTEXT_APP_NAME).exe
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
    UPDATE_EXE := $(UPDATE_APP_NAME)
    CONTEXT_EXE := $(CONTEXT_APP_NAME)
    INSTALL_DIR := /usr/local/bin
    USER_INSTALL_DIR := $(HOME)/.local/bin
    PUBLISH_DIR := publish/$(RID)
    PATHSEP := :
else ifeq ($(DETECTED_OS),Termux)
    RID := linux-bionic-$(ARCH)
    EXE := $(APP_NAME)
    UPDATE_EXE := $(UPDATE_APP_NAME)
    CONTEXT_EXE := $(CONTEXT_APP_NAME)
    INSTALL_DIR := $(PREFIX)/bin
    USER_INSTALL_DIR := $(HOME)/.local/bin
    PUBLISH_DIR := publish/$(RID)
    FIX_ELF_TLS := scripts/linux-termux-macos/fix-elf-tls.sh
    PATHSEP := :
else
    RID := linux-$(ARCH)
    EXE := $(APP_NAME)
    UPDATE_EXE := $(UPDATE_APP_NAME)
    CONTEXT_EXE := $(CONTEXT_APP_NAME)
    INSTALL_DIR := /usr/local/bin
    USER_INSTALL_DIR := $(HOME)/.local/bin
    PUBLISH_DIR := publish/$(RID)
    PATHSEP := :
endif

# Targets

.PHONY: all build release publish install install-user uninstall clean run test info help setfont deps

all: build

help:
ifeq ($(WIN_ENV),native)
	@cmd /c "echo."
	@echo   AurShell v$(VERSION) - Build System
	@echo   -------------------------------------
	@cmd /c "echo."
	@echo   make build          Debug build
	@echo   make release        Release build
	@echo   make deps           Check/install NativeAOT build dependencies
	@echo   make publish        Self-contained single-file release
	@echo   make install        Publish + install to system dir (needs admin)
	@echo   make install-user   Publish + install to user dir (no admin)
	@echo   make uninstall      Remove installed binary
	@echo   make clean          Remove all build artifacts
	@echo   make run            Build and launch interactive shell
	@echo   make setfont        Install JetBrains Mono NL Nerd Font on this OS
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
	@echo "  make deps           Check/install NativeAOT build dependencies"
	@echo "  make publish        Self-contained single-file release"
	@echo "  make install        Publish + install to system directory (may need sudo)"
	@echo "  make install-user   Publish + install to user directory (no sudo)"
	@echo "  make uninstall      Remove from system directory"
	@echo "  make clean          Remove all build artifacts"
	@echo "  make run            Build and launch interactive shell"
	@echo "  make setfont        Install JetBrains Mono NL Nerd Font on this OS"
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

# Build Targets

build:
ifeq ($(WIN_ENV),native)
	@echo [build] Compiling debug build...
	dotnet build $(PROJECT) -c Debug
	dotnet build $(CONTEXT_PROJECT) -c Debug
	@echo [build] Output: $(BIN_DIR)/$(EXE) + $(BIN_DIR)/$(CONTEXT_EXE)
else
	@echo "[build] Compiling debug build..."
	dotnet build $(PROJECT) -c Debug
	dotnet build $(CONTEXT_PROJECT) -c Debug
	@echo "[build] Output: $(BIN_DIR)/$(EXE) + $(BIN_DIR)/$(CONTEXT_EXE)"
endif

release:
ifeq ($(WIN_ENV),native)
	@echo [release] Compiling release build...
	dotnet build $(PROJECT) -c Release
	dotnet build $(CONTEXT_PROJECT) -c Release
	@echo [release] Output: $(BIN_DIR)/$(EXE) + $(BIN_DIR)/$(CONTEXT_EXE)
else
	@echo "[release] Compiling release build..."
	dotnet build $(PROJECT) -c Release
	dotnet build $(CONTEXT_PROJECT) -c Release
	@echo "[release] Output: $(BIN_DIR)/$(EXE) + $(BIN_DIR)/$(CONTEXT_EXE)"
endif

# Dependency Check

deps:
ifeq ($(WIN_ENV),native)
	@echo [deps] NativeAOT dependency check is not needed on Windows.
else
	@echo "[deps] Checking NativeAOT build dependencies..."
	@sh $(ENSURE_DEPS) --auto-install
endif

publish:
ifeq ($(WIN_ENV),native)
	@echo [publish] Publishing self-contained $(RID) binary...
	dotnet publish $(PROJECT) -c Release -r $(RID) --self-contained true -p:OutputPath=obj/publish-build/ -p:AppendTargetFrameworkToOutputPath=true -p:AppendRuntimeIdentifierToOutputPath=true -p:PublishAot=true -p:PublishTrimmed=true -o $(PUBLISH_DIR)
	dotnet publish $(UPDATE_PROJECT) -c Release -r $(RID) --self-contained true -p:OutputPath=obj/publish-build-update/ -p:AppendTargetFrameworkToOutputPath=true -p:AppendRuntimeIdentifierToOutputPath=true -p:PublishAot=true -p:PublishTrimmed=true -o $(PUBLISH_DIR)
	dotnet publish $(CONTEXT_PROJECT) -c Release -r $(RID) --self-contained true -p:OutputPath=obj/publish-build-contexts/ -p:AppendTargetFrameworkToOutputPath=true -p:AppendRuntimeIdentifierToOutputPath=true -p:PublishAot=true -p:PublishTrimmed=true -o $(PUBLISH_DIR)
	@echo [publish] Output: $(PUBLISH_DIR)/$(EXE) + $(PUBLISH_DIR)/$(UPDATE_EXE) + $(PUBLISH_DIR)/$(CONTEXT_EXE)
else
	@echo "[publish] Checking NativeAOT build dependencies..."
	@sh $(ENSURE_DEPS) --auto-install
	@echo "[publish] Publishing self-contained $(RID) binaries..."
	dotnet publish $(PROJECT) \
		-c Release \
		-r $(RID) \
		--self-contained true \
		-p:OutputPath=obj/publish-build/ \
		-p:AppendTargetFrameworkToOutputPath=true \
		-p:AppendRuntimeIdentifierToOutputPath=true \
		-p:PublishAot=true \
		-p:PublishTrimmed=true \
		-o $(PUBLISH_DIR)
	dotnet publish $(UPDATE_PROJECT) \
		-c Release \
		-r $(RID) \
		--self-contained true \
		-p:OutputPath=obj/publish-build-update/ \
		-p:AppendTargetFrameworkToOutputPath=true \
		-p:AppendRuntimeIdentifierToOutputPath=true \
		-p:PublishAot=true \
		-p:PublishTrimmed=true \
		-o $(PUBLISH_DIR)
	dotnet publish $(CONTEXT_PROJECT) \
		-c Release \
		-r $(RID) \
		--self-contained true \
		-p:OutputPath=obj/publish-build-contexts/ \
		-p:AppendTargetFrameworkToOutputPath=true \
		-p:AppendRuntimeIdentifierToOutputPath=true \
		-p:PublishAot=true \
		-p:PublishTrimmed=true \
		-o $(PUBLISH_DIR)
	@echo "[publish] Output: $(PUBLISH_DIR)/$(EXE) + $(PUBLISH_DIR)/$(UPDATE_EXE) + $(PUBLISH_DIR)/$(CONTEXT_EXE)"
ifeq ($(DETECTED_OS),Termux)
	@echo "[publish] Patching ELF TLS alignment for Android 15+ Bionic..."
	@sh $(FIX_ELF_TLS) "$(PUBLISH_DIR)/$(EXE)" "$(PUBLISH_DIR)/$(UPDATE_EXE)" "$(PUBLISH_DIR)/$(CONTEXT_EXE)"
endif
endif

# Install Targets

install: publish
ifeq ($(WIN_ENV),native)
	@echo [install] Installing to $(INSTALL_DIR)...
	@$(PS) "New-Item -Path '$(INSTALL_DIR)' -ItemType Directory -Force | Out-Null"
	@$(PS) "if (Test-Path '$(INSTALL_DIR)/$(EXE).old') { Remove-Item '$(INSTALL_DIR)/$(EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(INSTALL_DIR)/$(UPDATE_EXE).old') { Remove-Item '$(INSTALL_DIR)/$(UPDATE_EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(INSTALL_DIR)/$(CONTEXT_EXE).old') { Remove-Item '$(INSTALL_DIR)/$(CONTEXT_EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(INSTALL_DIR)/$(EXE)') { Rename-Item -Path '$(INSTALL_DIR)/$(EXE)' -NewName '$(EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(INSTALL_DIR)/$(UPDATE_EXE)') { Rename-Item -Path '$(INSTALL_DIR)/$(UPDATE_EXE)' -NewName '$(UPDATE_EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(INSTALL_DIR)/$(CONTEXT_EXE)') { Rename-Item -Path '$(INSTALL_DIR)/$(CONTEXT_EXE)' -NewName '$(CONTEXT_EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "Copy-Item -Path '$(PUBLISH_DIR)/$(EXE)' -Destination '$(INSTALL_DIR)/$(EXE)' -Force"
	@$(PS) "Copy-Item -Path '$(PUBLISH_DIR)/$(UPDATE_EXE)' -Destination '$(INSTALL_DIR)/$(UPDATE_EXE)' -Force"
	@$(PS) "Copy-Item -Path '$(PUBLISH_DIR)/$(CONTEXT_EXE)' -Destination '$(INSTALL_DIR)/$(CONTEXT_EXE)' -Force"
	@$(PS) "if (Test-Path '$(PUBLISH_DIR)/*.dll') { Copy-Item -Path '$(PUBLISH_DIR)/*.dll' -Destination '$(INSTALL_DIR)/' -Force }"
	@$(PS) "if (Test-Path '$(PUBLISH_DIR)/*.so') { Copy-Item -Path '$(PUBLISH_DIR)/*.so' -Destination '$(INSTALL_DIR)/' -Force }"
	@$(PS) "if (Test-Path '$(PUBLISH_DIR)/*.dylib') { Copy-Item -Path '$(PUBLISH_DIR)/*.dylib' -Destination '$(INSTALL_DIR)/' -Force }"
	@echo [install] Installed to $(INSTALL_DIR)/$(EXE) + $(INSTALL_DIR)/$(UPDATE_EXE) + $(INSTALL_DIR)/$(CONTEXT_EXE)
	@cmd /c "echo."
	@$(PS) "$$p = [System.Environment]::GetEnvironmentVariable('PATH','Machine'); if ($$p -notlike '*$(INSTALL_DIR)*') { [System.Environment]::SetEnvironmentVariable('PATH', $$p + ';$(INSTALL_DIR)', 'Machine'); Write-Host '  PATH updated (Machine). Restart your terminal.' } else { Write-Host '  $(INSTALL_DIR) is already in PATH.' }"
	@cmd /c "echo."
else ifeq ($(DETECTED_OS),Windows)
	@echo "[install] Installing to $(INSTALL_DIR)..."
	mkdir -p "$(INSTALL_DIR)"
	cp "$(PUBLISH_DIR)/$(EXE)" "$(INSTALL_DIR)/$(EXE)"
	cp "$(PUBLISH_DIR)/$(UPDATE_EXE)" "$(INSTALL_DIR)/$(UPDATE_EXE)"
	cp "$(PUBLISH_DIR)/$(CONTEXT_EXE)" "$(INSTALL_DIR)/$(CONTEXT_EXE)"
	-cp "$(PUBLISH_DIR)/"*.dll "$(PUBLISH_DIR)/"*.so "$(PUBLISH_DIR)/"*.dylib "$(INSTALL_DIR)/" 2>/dev/null || true
	@echo "[install] Installed to $(INSTALL_DIR)/$(EXE) + $(INSTALL_DIR)/$(UPDATE_EXE) + $(INSTALL_DIR)/$(CONTEXT_EXE)"
	@echo ""
	@powershell.exe -NoProfile -Command "$$p = [System.Environment]::GetEnvironmentVariable('PATH','Machine'); if ($$p -notlike '*$(INSTALL_DIR)*') { [System.Environment]::SetEnvironmentVariable('PATH', $$p + ';$(INSTALL_DIR)', 'Machine'); Write-Host '  PATH updated (Machine). Restart your terminal.' } else { Write-Host '  $(INSTALL_DIR) is already in PATH.' }"
	@echo ""
else
	@echo "[install] Installing to $(INSTALL_DIR)/$(EXE)..."
ifeq ($(DETECTED_OS),Termux)
	@echo "[install] Setting up glibc-compatibility symlinks for Bionic..."
	@sh scripts/linux-termux-macos/termux-compat.sh
endif
	install -d "$(INSTALL_DIR)"
	install -m 755 "$(PUBLISH_DIR)/$(EXE)" "$(INSTALL_DIR)/$(EXE)"
	install -m 755 "$(PUBLISH_DIR)/$(UPDATE_EXE)" "$(INSTALL_DIR)/$(UPDATE_EXE)"
	install -m 755 "$(PUBLISH_DIR)/$(CONTEXT_EXE)" "$(INSTALL_DIR)/$(CONTEXT_EXE)"
	-cp "$(PUBLISH_DIR)/"*.dll "$(PUBLISH_DIR)/"*.so "$(PUBLISH_DIR)/"*.dylib "$(INSTALL_DIR)/" 2>/dev/null || true
	@if [ -w "$(PREFIX)/etc/shells" ] && ! grep -Fxq "$(INSTALL_DIR)/$(EXE)" "$(PREFIX)/etc/shells"; then \
		echo "$(INSTALL_DIR)/$(EXE)" >> "$(PREFIX)/etc/shells"; \
		echo "[install] Added $(INSTALL_DIR)/$(EXE) to $(PREFIX)/etc/shells"; \
	elif [ -w /etc/shells ] && ! grep -Fxq "$(INSTALL_DIR)/$(EXE)" /etc/shells; then \
		echo "$(INSTALL_DIR)/$(EXE)" >> /etc/shells; \
		echo "[install] Added $(INSTALL_DIR)/$(EXE) to /etc/shells"; \
	fi
	@echo "[install] Installed to $(INSTALL_DIR)/$(EXE) + $(INSTALL_DIR)/$(UPDATE_EXE) + $(INSTALL_DIR)/$(CONTEXT_EXE)"
	@echo "[install] Run 'aursh' to start, or 'aursh-context' to manage contexts."
endif

install-user: publish
ifeq ($(WIN_ENV),native)
	@echo [install] Installing to $(USER_INSTALL_DIR)/$(EXE)...
	@$(PS) "New-Item -Path '$(USER_INSTALL_DIR)' -ItemType Directory -Force | Out-Null"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(EXE).old') { Remove-Item '$(USER_INSTALL_DIR)/$(EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(UPDATE_EXE).old') { Remove-Item '$(USER_INSTALL_DIR)/$(UPDATE_EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(CONTEXT_EXE).old') { Remove-Item '$(USER_INSTALL_DIR)/$(CONTEXT_EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(EXE)') { Rename-Item -Path '$(USER_INSTALL_DIR)/$(EXE)' -NewName '$(EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(UPDATE_EXE)') { Rename-Item -Path '$(USER_INSTALL_DIR)/$(UPDATE_EXE)' -NewName '$(UPDATE_EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(CONTEXT_EXE)') { Rename-Item -Path '$(USER_INSTALL_DIR)/$(CONTEXT_EXE)' -NewName '$(CONTEXT_EXE).old' -Force -ErrorAction SilentlyContinue }"
	@$(PS) "Copy-Item -Path '$(PUBLISH_DIR)/$(EXE)' -Destination '$(USER_INSTALL_DIR)/$(EXE)' -Force"
	@$(PS) "Copy-Item -Path '$(PUBLISH_DIR)/$(UPDATE_EXE)' -Destination '$(USER_INSTALL_DIR)/$(UPDATE_EXE)' -Force"
	@$(PS) "Copy-Item -Path '$(PUBLISH_DIR)/$(CONTEXT_EXE)' -Destination '$(USER_INSTALL_DIR)/$(CONTEXT_EXE)' -Force"
	@$(PS) "if (Test-Path '$(PUBLISH_DIR)/*.dll') { Copy-Item -Path '$(PUBLISH_DIR)/*.dll' -Destination '$(USER_INSTALL_DIR)/' -Force }"
	@$(PS) "if (Test-Path '$(PUBLISH_DIR)/*.so') { Copy-Item -Path '$(PUBLISH_DIR)/*.so' -Destination '$(USER_INSTALL_DIR)/' -Force }"
	@$(PS) "if (Test-Path '$(PUBLISH_DIR)/*.dylib') { Copy-Item -Path '$(PUBLISH_DIR)/*.dylib' -Destination '$(USER_INSTALL_DIR)/' -Force }"
	@echo [install] Installed to $(USER_INSTALL_DIR)/$(EXE) + $(USER_INSTALL_DIR)/$(UPDATE_EXE) + $(USER_INSTALL_DIR)/$(CONTEXT_EXE)
	@cmd /c "echo."
	@$(PS) "$$p = [System.Environment]::GetEnvironmentVariable('PATH','User'); if ($$p -notlike '*$(USER_INSTALL_DIR)*') { [System.Environment]::SetEnvironmentVariable('PATH', $$p + ';$(USER_INSTALL_DIR)', 'User'); Write-Host '  PATH updated. Restart your terminal to use aursh.' } else { Write-Host '  $(USER_INSTALL_DIR) is already in PATH.' }"
	@cmd /c "echo."
	@echo [install] Done. Run 'aursh' to start.
else ifeq ($(DETECTED_OS),Windows)
	@echo "[install] Installing to $(USER_INSTALL_DIR)/$(EXE)..."
	mkdir -p "$(USER_INSTALL_DIR)"
	cp "$(PUBLISH_DIR)/$(EXE)" "$(USER_INSTALL_DIR)/$(EXE)"
	cp "$(PUBLISH_DIR)/$(UPDATE_EXE)" "$(USER_INSTALL_DIR)/$(UPDATE_EXE)"
	cp "$(PUBLISH_DIR)/$(CONTEXT_EXE)" "$(USER_INSTALL_DIR)/$(CONTEXT_EXE)"
	-cp "$(PUBLISH_DIR)/"*.dll "$(PUBLISH_DIR)/"*.so "$(PUBLISH_DIR)/"*.dylib "$(USER_INSTALL_DIR)/" 2>/dev/null || true
	@echo "[install] Installed to $(USER_INSTALL_DIR)/$(EXE) + $(USER_INSTALL_DIR)/$(UPDATE_EXE) + $(USER_INSTALL_DIR)/$(CONTEXT_EXE)"
	@echo ""
	@powershell.exe -NoProfile -Command "$$p = [System.Environment]::GetEnvironmentVariable('PATH','User'); if ($$p -notlike '*$(USER_INSTALL_DIR)*') { [System.Environment]::SetEnvironmentVariable('PATH', $$p + ';$(USER_INSTALL_DIR)', 'User'); Write-Host '  PATH updated (User). Restart your terminal.' } else { Write-Host '  $(USER_INSTALL_DIR) is already in PATH.' }"
	@echo ""
	@echo "[install] Done. Run 'aursh' to start."
else
	@echo "[install] Installing to $(USER_INSTALL_DIR)/$(EXE)..."
	mkdir -p "$(USER_INSTALL_DIR)"
	cp "$(PUBLISH_DIR)/$(EXE)" "$(USER_INSTALL_DIR)/$(EXE)"
	cp "$(PUBLISH_DIR)/$(UPDATE_EXE)" "$(USER_INSTALL_DIR)/$(UPDATE_EXE)"
	cp "$(PUBLISH_DIR)/$(CONTEXT_EXE)" "$(USER_INSTALL_DIR)/$(CONTEXT_EXE)"
	-cp "$(PUBLISH_DIR)/"*.dll "$(PUBLISH_DIR)/"*.so "$(PUBLISH_DIR)/"*.dylib "$(USER_INSTALL_DIR)/" 2>/dev/null || true
	chmod +x "$(USER_INSTALL_DIR)/$(EXE)"
	chmod +x "$(USER_INSTALL_DIR)/$(UPDATE_EXE)"
	chmod +x "$(USER_INSTALL_DIR)/$(CONTEXT_EXE)"
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
	@$(PS) "if (Test-Path '$(INSTALL_DIR)/$(UPDATE_EXE)') { Remove-Item '$(INSTALL_DIR)/$(UPDATE_EXE)' -Force }"
	@$(PS) "if (Test-Path '$(INSTALL_DIR)/$(CONTEXT_EXE)') { Remove-Item '$(INSTALL_DIR)/$(CONTEXT_EXE)' -Force }"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(EXE)') { Remove-Item '$(USER_INSTALL_DIR)/$(EXE)' -Force }"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(UPDATE_EXE)') { Remove-Item '$(USER_INSTALL_DIR)/$(UPDATE_EXE)' -Force }"
	@$(PS) "if (Test-Path '$(USER_INSTALL_DIR)/$(CONTEXT_EXE)') { Remove-Item '$(USER_INSTALL_DIR)/$(CONTEXT_EXE)' -Force }"
	@echo [uninstall] Done.
else ifeq ($(DETECTED_OS),Windows)
	@echo "[uninstall] Removing aursh..."
	rm -f "$(INSTALL_DIR)/$(EXE)"
	rm -f "$(INSTALL_DIR)/$(UPDATE_EXE)"
	rm -f "$(INSTALL_DIR)/$(CONTEXT_EXE)"
	rm -f "$(USER_INSTALL_DIR)/$(EXE)"
	rm -f "$(USER_INSTALL_DIR)/$(UPDATE_EXE)"
	rm -f "$(USER_INSTALL_DIR)/$(CONTEXT_EXE)"
	@echo "[uninstall] Done."
else
	@echo "[uninstall] Removing aursh..."
	rm -f "$(INSTALL_DIR)/$(EXE)"
	rm -f "$(INSTALL_DIR)/$(UPDATE_EXE)"
	rm -f "$(INSTALL_DIR)/$(CONTEXT_EXE)"
	rm -f "$(USER_INSTALL_DIR)/$(EXE)"
	rm -f "$(USER_INSTALL_DIR)/$(UPDATE_EXE)"
	rm -f "$(USER_INSTALL_DIR)/$(CONTEXT_EXE)"
	@echo "[uninstall] Done."
endif

# Utility Targets

run: build
ifeq ($(WIN_ENV),native)
	@echo [run] Launching aursh...
	@$(PS) "& './$(BIN_DIR)/$(EXE)'"
else
	@echo "[run] Launching aursh..."
	@cd $(BIN_DIR) && ./$(EXE)
endif

test: build
ifeq ($(DETECTED_OS),Windows)
	@echo [test] Running tests...
	@python tests/test.py
else
	@echo "[test] Running tests..."
	@python3 tests/test.py
endif

clean:
ifeq ($(WIN_ENV),native)
	@echo [clean] Removing build artifacts...
	-@dotnet clean $(PROJECT) -c Debug --nologo -v q 2>nul
	-@dotnet clean $(PROJECT) -c Release --nologo -v q 2>nul
	-@dotnet clean $(UPDATE_PROJECT) -c Debug --nologo -v q 2>nul
	-@dotnet clean $(UPDATE_PROJECT) -c Release --nologo -v q 2>nul
	-@dotnet clean $(CONTEXT_PROJECT) -c Debug --nologo -v q 2>nul
	-@dotnet clean $(CONTEXT_PROJECT) -c Release --nologo -v q 2>nul
	@$(PS) "if (Test-Path '$(BIN_DIR)') { Remove-Item '$(BIN_DIR)/*' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'publish') { Remove-Item 'publish' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'src/obj') { Remove-Item 'src/obj' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'src/bin') { Remove-Item 'src/bin' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'src/aursh-update/obj') { Remove-Item 'src/aursh-update/obj' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'src/aursh-update/bin') { Remove-Item 'src/aursh-update/bin' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'src/Contexts/obj') { Remove-Item 'src/Contexts/obj' -Recurse -Force -ErrorAction SilentlyContinue }"
	@$(PS) "if (Test-Path 'src/Contexts/bin') { Remove-Item 'src/Contexts/bin' -Recurse -Force -ErrorAction SilentlyContinue }"
	@echo [clean] Done.
else
	@echo "[clean] Removing build artifacts..."
	dotnet clean $(PROJECT) -c Debug --nologo -v q 2>/dev/null || true
	dotnet clean $(PROJECT) -c Release --nologo -v q 2>/dev/null || true
	dotnet clean $(UPDATE_PROJECT) -c Debug --nologo -v q 2>/dev/null || true
	dotnet clean $(UPDATE_PROJECT) -c Release --nologo -v q 2>/dev/null || true
	dotnet clean $(CONTEXT_PROJECT) -c Debug --nologo -v q 2>/dev/null || true
	dotnet clean $(CONTEXT_PROJECT) -c Release --nologo -v q 2>/dev/null || true
	rm -rf $(BIN_DIR)/* 2>/dev/null || true
	rm -rf publish 2>/dev/null || true
	rm -rf src/obj 2>/dev/null || true
	rm -rf src/bin 2>/dev/null || true
	rm -rf src/aursh-update/obj 2>/dev/null || true
	rm -rf src/aursh-update/bin 2>/dev/null || true
	rm -rf src/Contexts/obj 2>/dev/null || true
	rm -rf src/Contexts/bin 2>/dev/null || true
	@echo "[clean] Done."
endif

# Font Installation

setfont:
ifeq ($(WIN_ENV),native)
	@echo [setfont] Installing $(FONT_FILE)...
	@$(PS) "$$src = '$(FONTS_DIR)/$(FONT_FILE)'; if (-not (Test-Path $$src)) { Write-Error \"Font file not found: $$src\"; exit 1 }; $$dst = \"$$env:LOCALAPPDATA\Microsoft\Windows\Fonts\"; New-Item -Path $$dst -ItemType Directory -Force | Out-Null; Copy-Item -Path $$src -Destination $$dst -Force; $$dstFile = Join-Path $$dst (Split-Path $$src -Leaf); $$regPath = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'; New-ItemProperty -Path $$regPath -Name 'JetBrainsMono NL Nerd Font Light (TrueType)' -Value $$dstFile -PropertyType String -Force | Out-Null; Write-Host \"  Installed: $$dstFile\"; Write-Host ''; Write-Host '  To use in Windows Terminal, edit settings.json and set:'; Write-Host '      \"fontFace\": \"JetBrainsMono NL Nerd Font\"'; Write-Host '  Or right-click Windows Terminal title bar > Settings > Defaults > Appearance > Font face.'"
else ifeq ($(DETECTED_OS),Windows)
	@echo "[setfont] Installing $(FONT_FILE)..."
	@if [ ! -f "$(FONTS_DIR)/$(FONT_FILE)" ]; then echo "[setfont] Font file not found: $(FONTS_DIR)/$(FONT_FILE)"; exit 1; fi
	@powershell.exe -NoProfile -Command "$$src = '$(FONTS_DIR)/$(FONT_FILE)'; $$dst = \"$$env:LOCALAPPDATA\Microsoft\Windows\Fonts\"; New-Item -Path $$dst -ItemType Directory -Force | Out-Null; Copy-Item -Path $$src -Destination $$dst -Force; $$dstFile = Join-Path $$dst (Split-Path $$src -Leaf); $$regPath = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'; New-ItemProperty -Path $$regPath -Name 'JetBrainsMono NL Nerd Font Light (TrueType)' -Value $$dstFile -PropertyType String -Force | Out-Null; Write-Host \"  Installed: $$dstFile\""
	@echo ""
	@echo "  To use in Windows Terminal, edit settings.json and set:"
	@echo '      "fontFace": "JetBrainsMono NL Nerd Font"'
	@echo ""
else ifeq ($(DETECTED_OS),macOS)
	@echo "[setfont] Installing $(FONT_FILE)..."
	@if [ ! -f "$(FONTS_DIR)/$(FONT_FILE)" ]; then echo "[setfont] Font file not found: $(FONTS_DIR)/$(FONT_FILE)"; exit 1; fi
	@mkdir -p "$(HOME)/Library/Fonts"
	@cp -f "$(FONTS_DIR)/$(FONT_FILE)" "$(HOME)/Library/Fonts/$(FONT_FILE)"
	@echo "[setfont] Installed: $(HOME)/Library/Fonts/$(FONT_FILE)"
	@echo ""
	@echo "  To use in Terminal.app:    Preferences > Profiles > Text > Change font"
	@echo "  To use in iTerm2:          Preferences > Profiles > Text > Font"
	@echo "  Pick 'JetBrainsMono NL Nerd Font'."
	@echo ""
else ifeq ($(DETECTED_OS),Termux)
	@echo "[setfont] Installing $(FONT_FILE) as Termux font..."
	@if [ ! -f "$(FONTS_DIR)/$(FONT_FILE)" ]; then echo "[setfont] Font file not found: $(FONTS_DIR)/$(FONT_FILE)"; exit 1; fi
	@mkdir -p "$(HOME)/.termux"
	@cp -f "$(FONTS_DIR)/$(FONT_FILE)" "$(HOME)/.termux/font.ttf"
	@echo "[setfont] Installed: $(HOME)/.termux/font.ttf"
	@echo ""
	@echo "  Run 'termux-reload-settings' to apply."
	@echo ""
else
	@echo "[setfont] Installing $(FONT_FILE)..."
	@if [ ! -f "$(FONTS_DIR)/$(FONT_FILE)" ]; then echo "[setfont] Font file not found: $(FONTS_DIR)/$(FONT_FILE)"; exit 1; fi
	@mkdir -p "$(HOME)/.local/share/fonts"
	@cp -f "$(FONTS_DIR)/$(FONT_FILE)" "$(HOME)/.local/share/fonts/$(FONT_FILE)"
	@echo "[setfont] Installed: $(HOME)/.local/share/fonts/$(FONT_FILE)"
	@if command -v fc-cache >/dev/null 2>&1; then \
		echo "[setfont] Rebuilding font cache (fc-cache -f)..."; \
		fc-cache -f "$(HOME)/.local/share/fonts" || true; \
	else \
		echo "[setfont] fc-cache not found; install 'fontconfig' for automatic cache refresh."; \
	fi
	@echo ""
	@echo "  To use it, set your terminal's font to 'JetBrainsMono NL Nerd Font':"
	@echo "    GNOME Terminal:  Preferences > Profile > Text > Custom font"
	@echo "    Konsole:         Settings > Edit Profile > Appearance > Font"
	@echo "    Alacritty:       set font.normal.family in ~/.config/alacritty/alacritty.toml"
	@echo "    Kitty:           set font_family in ~/.config/kitty/kitty.conf"
	@echo "    WezTerm:         set font in ~/.wezterm.lua"
	@echo ""
endif
