import React, { useState } from 'react';
import type { Track, UserData } from '../types';
import { FALLBACK_COVER } from '../constants';

interface RightSidebarProps {
  currentTrack: Track | null;
  userData: UserData | null;
  refreshUserData: () => void;
}

export const RightSidebar: React.FC<RightSidebarProps> = ({ currentTrack, userData, refreshUserData }) => {
  const [showPlaylistMenu, setShowPlaylistMenu] = useState(false);

  const handleAddToPlaylist = async (playlistId: string) => {
    if (!currentTrack) return;
    try {
      await fetch(`/api/playlist/${playlistId}/add/${currentTrack.id}`, { method: 'POST' });
      refreshUserData();
      setShowPlaylistMenu(false);
    } catch (e) { console.error(e); }
  };

  return (
    <div className="panel right-sidebar">
      <div className="right-sidebar-header">
        <span style={{ fontSize: '14px', color: 'var(--text-secondary)' }}>Now Playing</span>
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
            onError={(e) => { e.currentTarget.src = FALLBACK_COVER }}
          />
          
          <div className="now-playing-info-row">
            <div className="now-playing-text">
              <h2>{currentTrack.title}</h2>
              <p>{currentTrack.artist}</p>
            </div>
            <div style={{ display: 'flex', gap: '16px', color: 'var(--accent)', marginTop: '8px', position: 'relative' }}>
              <button className="circle-btn" style={{ background: 'transparent', border: 'none', cursor: 'pointer', padding: 0 }} onClick={() => setShowPlaylistMenu(!showPlaylistMenu)}>
                <svg viewBox="0 0 16 16" fill="currentColor" height="20" width="20" style={{ color: 'var(--text-secondary)' }}><path d="M12.5 1A2.5 2.5 0 0115 3.5v9A2.5 2.5 0 0112.5 15H3.5A2.5 2.5 0 011 12.5v-9A2.5 2.5 0 013.5 1h9zm0 1.5h-9A1 1 0 002.5 3.5v9A1 1 0 003.5 13.5h9a1 1 0 001-1v-9a1 1 0 00-1-1zM7 10.5v-6h1.5v6H7zM5 8h6v1.5H5V8z"/></svg>
              </button>
              {showPlaylistMenu && (
                <div style={{ position: 'absolute', top: '30px', right: '0', background: '#282828', padding: '4px', borderRadius: '4px', zIndex: 100, display: 'flex', flexDirection: 'column', minWidth: '150px', boxShadow: '0 4px 12px rgba(0,0,0,0.5)' }}>
                  <div style={{ padding: '8px', fontSize: '12px', fontWeight: 'bold', color: 'var(--text-secondary)', borderBottom: '1px solid #3E3E3E', marginBottom: '4px' }}>Add to playlist</div>
                  {userData?.playlists.length === 0 ? <div style={{ padding: '8px', color: 'var(--text-secondary)', fontSize: '13px' }}>No playlists</div> : null}
                  {userData?.playlists.map(pl => (
                    <button key={pl.id} onClick={() => handleAddToPlaylist(pl.id)} style={{ background: 'transparent', color: '#fff', border: 'none', padding: '12px', textAlign: 'left', cursor: 'pointer', borderRadius: '2px', fontSize: '13px' }}>{pl.name}</button>
                  ))}
                </div>
              )}
            </div>
          </div>

          <div className="artist-card" style={{ marginTop: '16px', padding: '16px', display: 'block' }}>
            <span style={{ fontSize: '14px', fontWeight: 700, marginBottom: '12px', display: 'block' }}>Track Information</span>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', fontSize: '13px', color: 'var(--text-secondary)' }}>
              <div><strong>Album:</strong> <br/>{currentTrack.album}</div>
              <div><strong>Year:</strong> <br/>{currentTrack.year || 'Unknown'}</div>
              <div><strong>Genre:</strong> <br/>{currentTrack.genre || 'Unknown'}</div>
              <div><strong>Track:</strong> <br/>{currentTrack.trackNumber || '-'}</div>
              <div><strong>Bitrate:</strong> <br/>{currentTrack.bitrate ? `${currentTrack.bitrate} kbps` : 'Unknown'}</div>
              <div><strong>Sample Rate:</strong> <br/>{currentTrack.sampleRate ? `${currentTrack.sampleRate} Hz` : 'Unknown'}</div>
              <div style={{ gridColumn: '1 / -1' }}><strong>Channels:</strong> <br/>{currentTrack.channels || 'Unknown'}</div>
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
