import React from 'react';
import type { Track, Status, UserData, ViewState } from '../types';
import { FALLBACK_COVER } from '../constants';
import { TrackOptionsMenu } from './TrackOptionsMenu';
import { PlaylistHeader } from './PlaylistHeader';

interface MainViewProps {
  status: Status | null;
  tracks: Track[];
  playTrack: (track: Track) => void;
  userData: UserData | null;
  refreshUserData: () => void;
  refreshTracks: () => void;
  currentView: ViewState;
  currentPlaylistId: string | null;
}

export const MainView: React.FC<MainViewProps> = ({ 
  status, tracks, playTrack, userData, refreshUserData, refreshTracks, currentView, currentPlaylistId 
}) => {
  if (!status) {
    return <div className="panel main-content" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center' }}>Connecting to AurSh...</div>;
  }

  if (!status.configured) {
    return (
      <div className="panel main-content">
        <div className="main-header-bg"></div>
        <div className="setup-container">
          <h2>Welcome to AurSh Music</h2>
          <p>Please use `aursh-music set-dir` to configure your music directory.</p>
        </div>
      </div>
    );
  }

  if (tracks.length === 0) {
    return (
      <div className="panel main-content">
        <div className="main-header-bg"></div>
        <div className="setup-container">
          <h2>Your Library is Empty</h2>
          <p>Add some music to {status.directory}</p>
        </div>
      </div>
    );
  }

  const renderTrackList = (trackList: Track[]) => (
    <div style={{ marginTop: '24px' }}>
      <div style={{ display: 'grid', gridTemplateColumns: '16px 2fr 1fr 1fr 40px', gap: '16px', color: 'var(--text-secondary)', fontSize: '14px', borderBottom: '1px solid #282828', paddingBottom: '8px', marginBottom: '16px' }}>
        <span>#</span>
        <span>Title</span>
        <span>Album</span>
        <span>Date added</span>
        <span></span>
      </div>
      {trackList.map((track, i) => (
        <div key={track.id} className="track-list-row" style={{ display: 'grid', gridTemplateColumns: '16px 2fr 1fr 1fr 40px', gap: '16px', alignItems: 'center', padding: '8px 0', borderRadius: '4px', cursor: 'pointer', position: 'relative', zIndex: 1000 - i }}>
          <span style={{ color: 'var(--text-secondary)' }}>{i + 1}</span>
          <div style={{ display: 'flex', gap: '12px', alignItems: 'center' }} onClick={() => playTrack(track)}>
            <img src={`/api/cover/${track.id}`} onError={(e) => { e.currentTarget.src = FALLBACK_COVER }} style={{ width: '40px', height: '40px', borderRadius: '4px', objectFit: 'cover' }} alt="cover" />
            <div style={{ display: 'flex', flexDirection: 'column' }}>
              <span style={{ color: '#fff' }}>{track.title}</span>
              <span style={{ color: 'var(--text-secondary)', fontSize: '14px' }}>{track.artist}</span>
            </div>
          </div>
          <span style={{ color: 'var(--text-secondary)', fontSize: '14px' }}>{track.album}</span>
          <span style={{ color: 'var(--text-secondary)', fontSize: '14px' }}>Today</span>
          <TrackOptionsMenu 
            track={track} 
            userData={userData} 
            refreshUserData={refreshUserData} 
            refreshTracks={refreshTracks}
            currentPlaylistId={currentView === 'Playlist' ? currentPlaylistId : null} 
          />
        </div>
      ))}
      <style>{`
        .track-list-row:hover {
          background-color: #2a2a2a;
        }
      `}</style>
    </div>
  );

  if (currentView === 'Liked') {
    const likedTracks = tracks.filter(t => userData?.likedTracks.includes(t.id));
    return (
      <div className="panel main-content">
        <div className="main-header-bg" style={{ background: 'linear-gradient(transparent, rgba(0,0,0,1))' }}></div>
        <div style={{ position: 'relative', zIndex: 1, padding: '16px 24px' }}>
          <div style={{ display: 'flex', gap: '24px', alignItems: 'flex-end', marginBottom: '24px' }}>
            <div style={{ width: '232px', height: '232px', background: 'linear-gradient(135deg, #450af5, #c4efd9)', display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: '0 4px 60px rgba(0,0,0,.5)' }}>
              <svg viewBox="0 0 24 24" fill="#fff" height="80" width="80"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/></svg>
            </div>
            <div>
              <p style={{ fontSize: '14px', fontWeight: 700, margin: '0 0 8px 0' }}>Playlist</p>
              <h1 style={{ fontSize: '96px', margin: '0 0 16px 0', lineHeight: '1.1' }}>Liked Songs</h1>
              <div style={{ fontSize: '14px' }}>
                <span style={{ fontWeight: 'bold' }}>You</span> • {likedTracks.length} songs
              </div>
            </div>
          </div>
          {renderTrackList(likedTracks)}
        </div>
      </div>
    );
  }

  if (currentView === 'Playlist' && currentPlaylistId) {
    const playlist = userData?.playlists.find(p => p.id === currentPlaylistId);
    if (!playlist) return <div className="panel main-content">Playlist not found</div>;
    const playlistTracks = playlist.trackIds.map(id => tracks.find(t => t.id === id)).filter(Boolean) as Track[];
    
    return (
      <div className="panel main-content">
        <div className="main-header-bg"></div>
        <div style={{ position: 'relative', zIndex: 1, padding: '16px 24px' }}>
          <PlaylistHeader playlist={playlist} refreshUserData={refreshUserData} />
          {renderTrackList(playlistTracks)}
        </div>
      </div>
    );
  }

  // Home View
  return (
    <div className="panel main-content">
      <div className="main-header-bg"></div>
      
      <div style={{ position: 'relative', zIndex: 1, padding: '16px 24px' }}>
        <div className="chips-row" style={{ padding: '0 0 24px 0' }}>
          <button className="chip" style={{ background: '#fff', color: '#000' }}>All</button>
          <button className="chip">Music</button>
          <button className="chip">Podcasts</button>
        </div>

        <div className="recent-grid">
          {tracks.slice(0, 6).map((track, i) => (
            <div key={track.id} className="recent-item" style={{ position: 'relative', overflow: 'visible', zIndex: 1000 - i }}>
              <div style={{ display: 'flex', alignItems: 'center', width: '100%' }} onClick={() => playTrack(track)}>
                <img 
                  src={`/api/cover/${track.id}`} 
                  alt="cover" 
                  onError={(e) => { e.currentTarget.src = FALLBACK_COVER }}
                />
                <span className="recent-item-title">{track.title}</span>
              </div>
              <div style={{ position: 'absolute', right: '8px', top: '50%', transform: 'translateY(-50%)' }}>
                <TrackOptionsMenu track={track} userData={userData} refreshUserData={refreshUserData} refreshTracks={refreshTracks} />
              </div>
            </div>
          ))}
        </div>

        <div className="section-title-row">
          <h2 className="section-title">Pre-save upcoming releases</h2>
          <span className="show-all">Show all</span>
        </div>
        
        <div className="card-grid">
          {tracks.slice(0, 4).map((track, i) => (
            <div key={track.id} className="card" style={{ position: 'relative', zIndex: 1000 - i }}>
              <div className="card-img-wrapper" onClick={() => playTrack(track)}>
                <img 
                  src={`/api/cover/${track.id}`} 
                  alt="cover"
                  onError={(e) => { e.currentTarget.src = FALLBACK_COVER }}
                />
                <button className="card-play-btn">
                  <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24"><path d="M8 5.14v14l11-7-11-7z"/></svg>
                </button>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginTop: '12px' }}>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="card-title" onClick={() => playTrack(track)}>{track.title}</div>
                  <div className="card-subtitle" onClick={() => playTrack(track)}>{track.artist}</div>
                </div>
                <div style={{ marginTop: '-4px' }}>
                  <TrackOptionsMenu track={track} userData={userData} refreshUserData={refreshUserData} refreshTracks={refreshTracks} />
                </div>
              </div>
            </div>
          ))}
        </div>

        <div className="section-title-row">
          <h2 className="section-title">Albums featuring songs you like</h2>
          <span className="show-all">Show all</span>
        </div>
        
        <div className="card-grid">
          {tracks.slice().reverse().slice(0, 4).map((track, i) => (
            <div key={track.id} className="card" style={{ position: 'relative', zIndex: 1000 - i }}>
              <div className="card-img-wrapper" onClick={() => playTrack(track)}>
                <img 
                  src={`/api/cover/${track.id}`} 
                  alt="cover"
                  onError={(e) => { e.currentTarget.src = FALLBACK_COVER }}
                />
                <button className="card-play-btn">
                  <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24"><path d="M8 5.14v14l11-7-11-7z"/></svg>
                </button>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginTop: '12px' }}>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="card-title" onClick={() => playTrack(track)}>{track.title}</div>
                  <div className="card-subtitle" onClick={() => playTrack(track)}>{track.artist}</div>
                </div>
                <div style={{ marginTop: '-4px' }}>
                  <TrackOptionsMenu track={track} userData={userData} refreshUserData={refreshUserData} refreshTracks={refreshTracks} />
                </div>
              </div>
            </div>
          ))}
        </div>
        
        <div style={{ height: '32px' }}></div>
      </div>
    </div>
  );
};
