import React, { useState, useRef, useEffect } from 'react';
import type { Track, UserData } from '../types';

interface TrackOptionsMenuProps {
  track: Track;
  userData: UserData | null;
  refreshUserData: () => void;
  refreshTracks: () => void;
  currentPlaylistId?: string | null;
  placement?: 'left' | 'top';
}

export const TrackOptionsMenu: React.FC<TrackOptionsMenuProps> = ({ 
  track, userData, refreshUserData, refreshTracks, currentPlaylistId, placement = 'left' 
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const [showPlaylists, setShowPlaylists] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
        setIsOpen(false);
        setShowPlaylists(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const handleAddToPlaylist = async (playlistId: string) => {
    try {
      await fetch(`/api/playlist/${playlistId}/add/${track.id}`, { method: 'POST' });
      refreshUserData();
      setIsOpen(false);
      setShowPlaylists(false);
    } catch (e) {
      console.error(e);
    }
  };

  const handleRemoveFromPlaylist = async () => {
    if (!currentPlaylistId) return;
    try {
      await fetch(`/api/playlist/${currentPlaylistId}/remove/${track.id}`, { method: 'DELETE' });
      refreshUserData();
      setIsOpen(false);
    } catch (e) {
      console.error(e);
    }
  };

  const handleDeleteSong = async () => {
    if (!confirm(`Are you sure you want to permanently delete "${track.title}" from your hard drive?`)) {
      return;
    }
    try {
      await fetch(`/api/tracks/${track.id}`, { method: 'DELETE' });
      refreshUserData();
      refreshTracks();
      setIsOpen(false);
    } catch (e) {
      console.error(e);
    }
  };

  return (
    <div className="track-options-wrapper" ref={menuRef} style={{ position: 'relative' }}>
      <button 
        className="options-btn" 
        onClick={(e) => {
          e.stopPropagation();
          setIsOpen(!isOpen);
          setShowPlaylists(false);
        }}
        style={{
          background: 'transparent',
          border: 'none',
          color: 'var(--text-secondary)',
          cursor: 'pointer',
          padding: '8px'
        }}
      >
        <svg viewBox="0 0 24 24" fill="currentColor" height="20" width="20">
          <path d="M12 4.5a1.5 1.5 0 110 3 1.5 1.5 0 010-3zm0 6a1.5 1.5 0 110 3 1.5 1.5 0 010-3zm0 6a1.5 1.5 0 110 3 1.5 1.5 0 010-3z"/>
        </svg>
      </button>

      {isOpen && (
        <div style={{
          position: 'absolute',
          ...(placement === 'top' ? { bottom: '100%', left: 0, marginBottom: '8px' } : { right: '100%', top: 0, marginRight: '8px' }),
          background: '#282828',
          borderRadius: '4px',
          boxShadow: '0 4px 12px rgba(0,0,0,0.5)',
          zIndex: 1000,
          minWidth: '200px',
          padding: '4px'
        }}>
          {!showPlaylists ? (
            <>
              <button 
                className="menu-item-btn" 
                onClick={(e) => { e.stopPropagation(); setShowPlaylists(true); }}
              >
                Add to playlist
                <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16" style={{marginLeft:'auto'}}><path d="M5.5 13L11 8 5.5 3v10z"/></svg>
              </button>
              {currentPlaylistId && (
                <button 
                  className="menu-item-btn"
                  onClick={(e) => { e.stopPropagation(); handleRemoveFromPlaylist(); }}
                >
                  Remove from this playlist
                </button>
              )}
              <div style={{ height: '1px', background: '#3E3E3E', margin: '4px 0' }} />
              <button 
                className="menu-item-btn" 
                style={{ color: '#E87C7C' }}
                onClick={(e) => { e.stopPropagation(); handleDeleteSong(); }}
              >
                Delete song
              </button>
            </>
          ) : (
            <>
              <div style={{ padding: '8px', color: 'var(--text-secondary)', fontSize: '12px', fontWeight: 'bold' }}>
                Add to playlist
              </div>
              <div style={{ height: '1px', background: '#3E3E3E', margin: '4px 0' }} />
              {userData?.playlists.map(pl => (
                <button 
                  key={pl.id} 
                  className="menu-item-btn"
                  onClick={(e) => { e.stopPropagation(); handleAddToPlaylist(pl.id); }}
                >
                  {pl.name}
                </button>
              ))}
            </>
          )}
        </div>
      )}
      <style>{`
        .menu-item-btn {
          width: 100%;
          text-align: left;
          background: transparent;
          color: #fff;
          border: none;
          padding: 12px;
          cursor: pointer;
          border-radius: 2px;
          display: flex;
          align-items: center;
          font-size: 14px;
        }
        .menu-item-btn:hover {
          background: #3E3E3E;
        }
      `}</style>
    </div>
  );
};
