import React from 'react';
import type { Track } from '../types';

interface SidebarProps {
  tracks: Track[];
  playTrack: (track: Track) => void;
}

export const Sidebar: React.FC<SidebarProps> = ({ tracks, playTrack }) => {
  return (
    <div className="panel left-sidebar">
      <div className="sidebar-header">
        <div className="sidebar-header-left">
          <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24">
            <path d="M14.5 2.134a1 1 0 011 0l6 3.464a1 1 0 01.5.866V21a1 1 0 01-1 1h-6a1 1 0 01-1-1V3a1 1 0 01.5-.866zM16 4.732V20h4V7.041l-4-2.309zM3 22a1 1 0 01-1-1V3a1 1 0 012 0v18a1 1 0 01-1 1zm6 0a1 1 0 01-1-1V3a1 1 0 012 0v18a1 1 0 01-1 1z"/>
          </svg>
          Your Library
        </div>
        <div style={{ display: 'flex', gap: '16px' }}>
          <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M15.25 8a.75.75 0 01-.75.75H8.75v5.75a.75.75 0 01-1.5 0V8.75H1.5a.75.75 0 010-1.5h5.75V1.5a.75.75 0 011.5 0v5.75h5.75a.75.75 0 01.75.75z"/></svg>
          <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M7.19 1A1.449 1.449 0 018.479.217l1.011 1.752a1.449 1.449 0 001.282.72h2.025a1.45 1.45 0 011.45 1.45v2.025a1.45 1.45 0 00.72 1.282l1.752 1.011a1.45 1.45 0 010 2.518l-1.752 1.011a1.45 1.45 0 00-.72 1.282v2.025a1.45 1.45 0 01-1.45 1.45h-2.025a1.45 1.45 0 00-1.282.72l-1.011 1.752a1.45 1.45 0 01-2.518 0l-1.011-1.752a1.45 1.45 0 00-1.282-.72H3.227a1.45 1.45 0 01-1.45-1.45v-2.025a1.45 1.45 0 00-.72-1.282L-.25 8.479a1.45 1.45 0 010-2.518l1.752-1.011a1.45 1.45 0 00.72-1.282V1.667A1.45 1.45 0 013.673.217h2.025a1.45 1.45 0 001.282-.72L8 .25l-.81.75zM11.5 8a3.5 3.5 0 10-7 0 3.5 3.5 0 007 0zm-1.5 0a2 2 0 11-4 0 2 2 0 014 0z"/></svg>
        </div>
      </div>

      <div className="chips-row">
        <button className="chip">Playlists</button>
        <button className="chip">Albums</button>
        <button className="chip">Artists</button>
      </div>

      <div style={{ padding: '8px 16px', display: 'flex', justifyContent: 'space-between', color: 'var(--text-secondary)' }}>
        <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M7 1.75a5.25 5.25 0 100 10.5 5.25 5.25 0 000-10.5zM.25 7a6.75 6.75 0 1112.096 4.12l3.184 3.185a.75.75 0 11-1.06 1.06L11.304 12.2A6.75 6.75 0 01.25 7z"/></svg>
        <span style={{ fontSize: '13px', display: 'flex', alignItems: 'center', gap: '4px' }}>
          Recents <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M14 6L8 12 2 6h12z"/></svg>
        </span>
      </div>

      <div className="library-list">
        <div className="library-item" style={{ background: 'var(--bg-elevated)' }}>
          <div style={{ width: 48, height: 48, borderRadius: 4, background: 'linear-gradient(135deg, #450af5, #c4efd9)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <svg viewBox="0 0 24 24" fill="#fff" height="20" width="20"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/></svg>
          </div>
          <div className="library-info">
            <span className="library-title">Liked Songs</span>
            <span className="library-subtext" style={{ color: 'var(--accent)' }}>📌 Playlist • User</span>
          </div>
        </div>

        {tracks.map(track => (
          <div key={track.id} className="library-item" onClick={() => playTrack(track)}>
            <img 
              src={`/api/cover/${track.id}`} 
              alt={track.title} 
              className="library-cover"
              onError={(e) => { e.currentTarget.src = 'https://via.placeholder.com/48?text=No+Cover' }}
            />
            <div className="library-info">
              <span className="library-title">{track.title}</span>
              <span className="library-subtext">Song • {track.artist}</span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};
