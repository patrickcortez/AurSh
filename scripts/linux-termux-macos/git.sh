#!/usr/bin/env bash

# git helper

set -e

help() {
    echo "Help:"
    echo "  Commit <message>  : add and commit"
    echo "  Push <branch>     : push to remote repository"
}

commit() {
    if command -v git >/dev/null 2>&1; then

        echo "Staging repository..."
        git add -A

        echo "Committing changes..."
        git commit -m "$1"

    else
        echo "Git not found!"
        exit 1
    fi
}

push() {
    if command -v git >/dev/null 2>&1; then

        if [[ -z "$1" ]]; then
            echo "Branch cannot be empty!"
            exit 1
        fi

        echo "Pushing to branch $1"
        git push origin "$1"

    else
        echo "Git is not installed"
        exit 1
    fi
}

case "$1" in
    Commit)
        commit "$2"
        ;;
    
    Push)
        push "$2"
        ;;
    
    ""|-h|--help|help)
        help
        ;;
    
    *)
        echo "Unknown command: $1"
        echo
        help
        exit 1
        ;;
esac