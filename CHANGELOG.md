# Changelog

## [Unreleased]
### Added
- Dynamic loading of music tracks with `FileSystemWatcher`
- Stable track IDs based on file paths (Base64) to support dynamic reloading without playback interruption
- `UserDataManager` to persist user playlists and liked tracks safely
- Complete Play/Pause/Next/Previous/Shuffle/Repeat playback controls in the frontend UI
- Search, Liked Songs, and Library/Playlist views in the UI
- React frontend strictly modularized into `Sidebar`, `PlayerBar`, and `MainView` components

### Changed
- Refactored `App.tsx` from a monolithic file into distinct reusable components.
- Added strict type checking with `types.ts` using `verbatimModuleSyntax`.
- Embedded `music_ui.zip` directly into the `.NET` binary, allowing end-users to use the shell without Node.js/NPM dependencies.
