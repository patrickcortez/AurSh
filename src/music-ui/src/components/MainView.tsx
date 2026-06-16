import React from 'react';
import type { Track, Status } from '../types';

interface MainViewProps {
  status: Status | null;
  tracks: Track[];
  playTrack: (track: Track) => void;
}

export const MainView: React.FC<MainViewProps> = ({ status, tracks, playTrack }) => {
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
          {tracks.slice(0, 6).map(track => (
            <div key={track.id} className="recent-item" onClick={() => playTrack(track)}>
              <img 
                src={`/api/cover/${track.id}`} 
                alt="cover" 
                onError={(e) => { e.currentTarget.src = 'https://via.placeholder.com/64?text=Cover' }}
              />
              <span className="recent-item-title">{track.title}</span>
            </div>
          ))}
        </div>

        <div className="section-title-row">
          <h2 className="section-title">Pre-save upcoming releases</h2>
          <span className="show-all">Show all</span>
        </div>
        
        <div className="card-grid">
          {tracks.slice(0, 4).map(track => (
            <div key={track.id} className="card" onClick={() => playTrack(track)}>
              <div className="card-img-wrapper">
                <img 
                  src={`/api/cover/${track.id}`} 
                  alt="cover"
                  onError={(e) => { e.currentTarget.src = 'https://via.placeholder.com/200?text=Cover' }}
                />
                <button className="card-play-btn">
                  <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24"><path d="M8 5.14v14l11-7-11-7z"/></svg>
                </button>
              </div>
              <div className="card-title">{track.title}</div>
              <div className="card-subtitle">{track.artist}</div>
            </div>
          ))}
        </div>

        <div className="section-title-row">
          <h2 className="section-title">Albums featuring songs you like</h2>
          <span className="show-all">Show all</span>
        </div>
        
        <div className="card-grid">
          {tracks.slice().reverse().slice(0, 4).map(track => (
            <div key={track.id} className="card" onClick={() => playTrack(track)}>
              <div className="card-img-wrapper">
                <img 
                  src={`/api/cover/${track.id}`} 
                  alt="cover"
                  onError={(e) => { e.currentTarget.src = 'https://via.placeholder.com/200?text=Cover' }}
                />
                <button className="card-play-btn">
                  <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24"><path d="M8 5.14v14l11-7-11-7z"/></svg>
                </button>
              </div>
              <div className="card-title">{track.title}</div>
              <div className="card-subtitle">{track.artist}</div>
            </div>
          ))}
        </div>
        
        <div style={{ height: '32px' }}></div>
      </div>
    </div>
  );
};
