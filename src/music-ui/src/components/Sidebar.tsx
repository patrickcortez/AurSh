import React, { useRef, useState } from 'react';
import type { Track, UserData, Playlist } from '../types';
import { FALLBACK_COVER } from '../constants';
import { EditPlaylistModal } from './EditPlaylistModal';
import { SettingsModal } from './SettingsModal';

interface SidebarProps {
  tracks: Track[];
  playTrack: (track: Track) => void;
  userData: UserData | null;
  refreshUserData: () => void;
  refreshTracks: () => void;
  setView: (view: import('../types').ViewState, playlistId?: string | null) => void;
}

export const Sidebar: React.FC<SidebarProps> = ({ tracks, playTrack, userData, refreshUserData, refreshTracks, setView }) => {
  const [showAddMenu, setShowAddMenu] = useState(false);
  const [editingPlaylist, setEditingPlaylist] = useState<Playlist | null>(null);
  const [activeMenu, setActiveMenu] = useState<string | null>(null);
  const [showSettings, setShowSettings] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleCreatePlaylist = async () => {
    setShowAddMenu(false);
    const name = prompt("Enter playlist name:");
    if (!name) return;
    try {
      await fetch('/api/playlist', { method: 'POST', body: name });
      refreshUserData();
    } catch (e) { console.error(e); }
  };

  const handleDeletePlaylist = async (pl: Playlist) => {
    if (!confirm(`Are you sure you want to delete '${pl.name}'?`)) return;
    try {
      await fetch(`/api/playlist/${pl.id}`, { method: 'DELETE' });
      refreshUserData();
      // Optionally could setView('Home') if currently on this playlist, but parent can handle if needed.
    } catch (e) { console.error(e); }
  };

  const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    setShowAddMenu(false);
    const files = e.target.files;
    if (!files || files.length === 0) return;
    const formData = new FormData();
    for (let i = 0; i < files.length; i++) {
      formData.append('files', files[i]);
    }
    try {
      await fetch('/api/upload', { method: 'POST', body: formData });
      refreshTracks();
    } catch (e) { console.error(e); }
  };
  return (
    <div className="panel left-sidebar">
      <div className="sidebar-header">
        <div className="sidebar-header-left">
          <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24">
            <path d="M14.5 2.134a1 1 0 011 0l6 3.464a1 1 0 01.5.866V21a1 1 0 01-1 1h-6a1 1 0 01-1-1V3a1 1 0 01.5-.866zM16 4.732V20h4V7.041l-4-2.309zM3 22a1 1 0 01-1-1V3a1 1 0 012 0v18a1 1 0 01-1 1zm6 0a1 1 0 01-1-1V3a1 1 0 012 0v18a1 1 0 01-1 1z"/>
          </svg>
          Your Library
        </div>
        <div style={{ display: 'flex', gap: '16px', position: 'relative' }}>
          <button className="circle-btn" style={{ background: 'transparent' }} onClick={() => setShowAddMenu(!showAddMenu)}>
            <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M15.25 8a.75.75 0 01-.75.75H8.75v5.75a.75.75 0 01-1.5 0V8.75H1.5a.75.75 0 010-1.5h5.75V1.5a.75.75 0 011.5 0v5.75h5.75a.75.75 0 01.75.75z"/></svg>
          </button>
          
          {showAddMenu && (
            <div style={{ position: 'absolute', top: '30px', right: '0', background: '#282828', padding: '4px', borderRadius: '4px', zIndex: 100, display: 'flex', flexDirection: 'column', minWidth: '150px', boxShadow: '0 4px 12px rgba(0,0,0,0.5)' }}>
              <button onClick={handleCreatePlaylist} style={{ background: 'transparent', color: '#fff', border: 'none', padding: '12px', textAlign: 'left', cursor: 'pointer', borderRadius: '2px' }}>Create a new playlist</button>
              <button onClick={() => fileInputRef.current?.click()} style={{ background: 'transparent', color: '#fff', border: 'none', padding: '12px', textAlign: 'left', cursor: 'pointer', borderRadius: '2px' }}>Upload local audio files</button>
            </div>
          )}
          <input type="file" multiple accept="audio/*" ref={fileInputRef} style={{ display: 'none' }} onChange={handleUpload} />
          
          <button className="circle-btn" style={{ background: 'transparent' }} onClick={() => setShowSettings(true)}>
            <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M7.19 1A1.449 1.449 0 018.479.217l1.011 1.752a1.449 1.449 0 001.282.72h2.025a1.45 1.45 0 011.45 1.45v2.025a1.45 1.45 0 00.72 1.282l1.752 1.011a1.45 1.45 0 010 2.518l-1.752 1.011a1.45 1.45 0 00-.72 1.282v2.025a1.45 1.45 0 01-1.45 1.45h-2.025a1.45 1.45 0 00-1.282.72l-1.011 1.752a1.45 1.45 0 01-2.518 0l-1.011-1.752a1.45 1.45 0 00-1.282-.72H3.227a1.45 1.45 0 01-1.45-1.45v-2.025a1.45 1.45 0 00-.72-1.282L-.25 8.479a1.45 1.45 0 010-2.518l1.752-1.011a1.45 1.45 0 00.72-1.282V1.667A1.45 1.45 0 013.673.217h2.025a1.45 1.45 0 001.282-.72L8 .25l-.81.75zM11.5 8a3.5 3.5 0 10-7 0 3.5 3.5 0 007 0zm-1.5 0a2 2 0 11-4 0 2 2 0 014 0z"/></svg>
          </button>
        </div>
      </div>

      <div className="library-list">
        <div className="library-item" style={{ background: 'var(--bg-elevated)', cursor: 'pointer' }} onClick={() => setView('Liked')}>
          <div style={{ width: 48, height: 48, borderRadius: 4, background: 'linear-gradient(135deg, #450af5, #c4efd9)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <svg viewBox="0 0 24 24" fill="#fff" height="20" width="20"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/></svg>
          </div>
          <div className="library-info">
            <span className="library-title">Liked Songs</span>
            <span className="library-subtext" style={{ color: 'var(--accent)' }}>📌 Playlist • {userData?.likedTracks.length || 0} songs</span>
          </div>
        </div>

        {userData?.playlists.map((pl, idx) => (
          <div 
            key={pl.id} 
            className="library-item" 
            style={{ background: 'var(--bg-elevated)', cursor: 'pointer', position: 'relative', zIndex: 100 - idx }} 
            onClick={() => setView('Playlist', pl.id)}
          >
            <div style={{ width: 48, height: 48, borderRadius: 4, background: '#282828', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
              <img src={pl.coverArt || FALLBACK_COVER} style={{ width: '100%', height: '100%', objectFit: 'cover' }} alt="cover" />
            </div>
            <div className="library-info" style={{ flex: 1, minWidth: 0, overflow: 'hidden' }}>
              <span className="library-title" style={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', display: 'block' }}>{pl.name}</span>
              <span className="library-subtext">Playlist • {pl.trackIds.length} tracks</span>
            </div>
            <div style={{ position: 'relative' }} className="playlist-menu-container">
              <button 
                onClick={(e) => {
                  e.stopPropagation();
                  setActiveMenu(activeMenu === pl.id ? null : pl.id);
                }}
                style={{ background: 'transparent', border: 'none', color: 'var(--text-secondary)', cursor: 'pointer', padding: '8px' }}
              >
                <svg viewBox="0 0 24 24" fill="currentColor" height="20" width="20">
                  <path d="M12 4.5a1.5 1.5 0 110 3 1.5 1.5 0 010-3zm0 6a1.5 1.5 0 110 3 1.5 1.5 0 010-3zm0 6a1.5 1.5 0 110 3 1.5 1.5 0 010-3z"/>
                </svg>
              </button>
              {activeMenu === pl.id && (
                <div style={{ position: 'absolute', right: 0, top: '100%', background: '#282828', padding: '4px', borderRadius: '4px', zIndex: 100, boxShadow: '0 4px 12px rgba(0,0,0,0.5)', minWidth: '150px' }}>
                  <button 
                    onClick={(e) => { e.stopPropagation(); setEditingPlaylist(pl); setActiveMenu(null); }} 
                    style={{ background: 'transparent', color: '#fff', border: 'none', padding: '12px', textAlign: 'left', cursor: 'pointer', borderRadius: '2px', width: '100%' }}
                    onMouseEnter={(e) => e.currentTarget.style.background = '#3E3E3E'}
                    onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
                  >
                    Edit details
                  </button>
                  <button 
                    onClick={(e) => { e.stopPropagation(); handleDeletePlaylist(pl); setActiveMenu(null); }} 
                    style={{ background: 'transparent', color: '#fff', border: 'none', padding: '12px', textAlign: 'left', cursor: 'pointer', borderRadius: '2px', width: '100%' }}
                    onMouseEnter={(e) => e.currentTarget.style.background = '#3E3E3E'}
                    onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
                  >
                    Delete playlist
                  </button>
                </div>
              )}
            </div>
          </div>
        ))}

        {tracks.map(track => (
          <div key={track.id} className="library-item" onClick={() => playTrack(track)}>
            <img 
              src={`/api/cover/${track.id}`} 
              alt={track.title} 
              className="library-cover"
              onError={(e) => { e.currentTarget.src = FALLBACK_COVER }}
            />
            <div className="library-info">
              <span className="library-title">{track.title}</span>
              <span className="library-subtext">Song • {track.artist}</span>
            </div>
          </div>
        ))}
      </div>

      {editingPlaylist && (
        <EditPlaylistModal
          playlist={editingPlaylist}
          isOpen={true}
          onClose={() => setEditingPlaylist(null)}
          onSave={refreshUserData}
        />
      )}

      <SettingsModal 
        isOpen={showSettings} 
        onClose={() => setShowSettings(false)} 
        refreshTracks={refreshTracks}
      />
    </div>
  );
};
