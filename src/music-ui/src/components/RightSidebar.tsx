import React from 'react';
import type { Track } from '../types';

interface RightSidebarProps {
  currentTrack: Track | null;
}

export const RightSidebar: React.FC<RightSidebarProps> = ({ currentTrack }) => {
  return (
    <div className="panel right-sidebar">
      <div className="right-sidebar-header">
        <span style={{ fontSize: '14px', color: 'var(--text-secondary)' }}>Music My life Runs...</span>
        <div style={{ display: 'flex', gap: '16px' }}>
          <span>...</span>
          <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M2.47 2.47a.75.75 0 011.06 0L8 6.94l4.47-4.47a.75.75 0 111.06 1.06L9.06 8l4.47 4.47a.75.75 0 11-1.06 1.06L8 9.06l-4.47 4.47a.75.75 0 01-1.06-1.06L6.94 8 2.47 3.53a.75.75 0 010-1.06z"/></svg>
        </div>
      </div>

      {currentTrack ? (
        <>
          <img 
            className="now-playing-large-art"
            src={`/api/cover/${currentTrack.id}`} 
            alt="Album Art" 
            onError={(e) => { e.currentTarget.src = 'https://via.placeholder.com/300?text=No+Cover' }}
          />
          
          <div className="now-playing-info-row">
            <div className="now-playing-text">
              <h2>{currentTrack.title}</h2>
              <p>{currentTrack.artist}</p>
            </div>
            <div style={{ display: 'flex', gap: '16px', color: 'var(--accent)', marginTop: '8px' }}>
              <svg viewBox="0 0 16 16" fill="currentColor" height="20" width="20" style={{ color: 'var(--text-secondary)' }}><path d="M12.5 1A2.5 2.5 0 0115 3.5v9A2.5 2.5 0 0112.5 15H3.5A2.5 2.5 0 011 12.5v-9A2.5 2.5 0 013.5 1h9zm0 1.5h-9A1 1 0 002.5 3.5v9A1 1 0 003.5 13.5h9a1 1 0 001-1v-9a1 1 0 00-1-1zM7 10.5v-6h1.5v6H7zM5 8h6v1.5H5V8z"/></svg>
              <svg viewBox="0 0 16 16" fill="currentColor" height="20" width="20"><path d="M8 1.5a6.5 6.5 0 100 13 6.5 6.5 0 000-13zM0 8a8 8 0 1116 0A8 8 0 010 8z"/><path d="M11.78 5.22a.75.75 0 010 1.06l-4.5 4.5a.75.75 0 01-1.06 0l-2-2a.75.75 0 111.06-1.06l1.47 1.47 3.97-3.97a.75.75 0 011.06 0z"/></svg>
            </div>
          </div>

          <div className="artist-card">
            <img 
              className="artist-card-img"
              src={`/api/cover/${currentTrack.id}`} 
              alt="Artist"
              onError={(e) => { e.currentTarget.src = 'https://via.placeholder.com/300x160?text=Artist' }}
            />
            <div className="artist-card-content">
              <span style={{ fontSize: '12px', fontWeight: 700 }}>About the artist</span>
              <div className="artist-card-title" style={{ marginTop: '16px' }}>{currentTrack.artist}</div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span style={{ color: 'var(--text-secondary)' }}>28,268,063<br/>monthly listeners</span>
                <button style={{ background: 'transparent', border: '1px solid #727272', color: '#fff', padding: '6px 16px', borderRadius: '500px', fontWeight: 700, cursor: 'pointer' }}>
                  Follow
                </button>
              </div>
            </div>
          </div>
        </>
      ) : (
        <div style={{ color: 'var(--text-secondary)', textAlign: 'center', marginTop: '64px' }}>
          No track playing
        </div>
      )}
    </div>
  );
};
