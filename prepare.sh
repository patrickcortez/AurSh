#!/bin/bash

# prepare script for mac, termux and linux

# Check if .NET 8.0 SDK is installed
echo "Checking for .NET 8.0 SDK..."
dotnet --version
if [ $? -ne 0 ]; then
    echo "Installing .NET 8.0 SDK..."
    sudo apt-get install -y dotnet-sdk-8.0
    echo ".NET 8.0 SDK is installed."
else
    echo ".NET 8.0 SDK is already installed."
fi

# Check if make is installed
echo "Checking for make..."
which make
if [ $? -ne 0 ]; then
    echo "Installing make..."
    sudo apt-get install -y make
    echo "make is installed."
else
    echo "make is already installed."
fi

# Building project
echo "Building AurSh..."
sudo make install >nul 2>&1
echo "AurSh has been built and installed."
