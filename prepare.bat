:: Windows setup script for AurShell

@echo off

:: Check if .NET 8.0 SDK is installed
echo "Checking for .NET 8.0 SDK..."
dotnet --version
if %errorlevel% neq 0 (
    echo "Installing .NET 8.0 SDK..."
    winget install --id Microsoft.DotNet.SDK.8
    echo ".NET 8.0 SDK is installed."
) else (
    echo ".NET 8.0 SDK is already installed."
)

:: Check if make is installed

echo "Checking for make..."
where make >nul 2>&1
if %errorlevel% neq 0 (
    echo "Installing make..."
    winget install --id GNU.Make
    echo "make is installed."
) else (
    echo "make is already installed."
)

:: Building AurSh

echo "Building AurSh..."

sudo make install >nul 2>&1

echo "AurSh has been built and installed."







