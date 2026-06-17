import React, { useState } from 'react';
import { FALLBACK_COVER } from '../constants';
import type { Playlist } from '../types';
import { EditPlaylistModal } from './EditPlaylistModal';

interface PlaylistHeaderProps {
  playlist: Playlist;
  refreshUserData: () => void;
}

export const PlaylistHeader: React.FC<PlaylistHeaderProps> = ({ playlist, refreshUserData }) => {
  const [isEditing, setIsEditing] = useState(false);

  return (
    <div style={{ display: 'flex', gap: '24px', alignItems: 'flex-end', marginBottom: '24px' }}>
      <div 
        style={{ width: '232px', height: '232px', cursor: 'pointer', position: 'relative', background: '#282828', display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: '0 4px 60px rgba(0,0,0,.5)' }}
        onClick={() => setIsEditing(true)}
      >
        <img 
          src={playlist.coverArt || FALLBACK_COVER} 
          style={{ width: '100%', height: '100%', objectFit: 'cover' }}
          alt="Playlist Cover" 
        />
        <div style={{ position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#fff', fontSize: '14px', flexDirection: 'column', opacity: 0, transition: 'opacity 0.2s' }} onMouseEnter={e => e.currentTarget.style.opacity = '1'} onMouseLeave={e => e.currentTarget.style.opacity = '0'}>
          <svg viewBox="0 0 24 24" fill="currentColor" height="48" width="48" style={{ marginBottom: '8px' }}><path d="M17.39 5.86l1.75 1.75L6 20.75H4.25V19l13.14-13.14zm0-2.83a2.5 2.5 0 00-1.77.73L2.5 16.88V23h6.12L21.74 9.88a2.5 2.5 0 000-3.53l-1.77-1.77a2.5 2.5 0 00-2.58-.49z"/></svg>
          Choose photo
        </div>
      </div>

      <div style={{ flex: 1, minWidth: 0 }}>
        <p style={{ fontSize: '14px', fontWeight: 700, margin: '0 0 8px 0' }}>Playlist</p>
        
        <h1 
          style={{ fontSize: 'clamp(32px, 5vw, 64px)', margin: '0 0 16px 0', cursor: 'pointer', lineHeight: '1.1', wordBreak: 'break-word' }}
          onClick={() => setIsEditing(true)}
        >
          {playlist.name}
        </h1>

        <p 
          style={{ color: 'var(--text-secondary)', cursor: 'pointer', fontSize: '14px', margin: 0 }}
          onClick={() => setIsEditing(true)}
        >
          {playlist.description || "Add an optional description"}
        </p>

        <div style={{ marginTop: '16px', fontSize: '14px' }}>
          <span style={{ fontWeight: 'bold' }}>You</span> • {playlist.trackIds.length} songs
        </div>
      </div>

      <EditPlaylistModal
        playlist={playlist}
        isOpen={isEditing}
        onClose={() => setIsEditing(false)}
        onSave={refreshUserData}
      />
    </div>
  );
};
