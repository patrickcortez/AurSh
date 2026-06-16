import React, { useState } from 'react';
import type { Track, Status, ViewState, UserData } from '../types';

interface MainViewProps {
  status: Status | null;
  tracks: Track[];
  userData: UserData | null;
  currentView: ViewState;
  onConfigSubmit: (e: React.FormEvent) => void;
  configInput: string;
  setConfigInput: (val: string) => void;
  playTrack: (track: Track) => void;
}

export const MainView: React.FC<MainViewProps> = ({
  status, tracks, userData, currentView, 
  onConfigSubmit, configInput, setConfigInput, playTrack
}) => {
  const [searchQuery, setSearchQuery] = useState('');

  if (status === null) {
    return (
      <div className="setup-container">
        <h2>Loading...</h2>
        <p>Connecting to AurSh Music daemon...</p>
      </div>
    );
  }

  if (status.configured === false || status.trackCount === 0) {
    return (
      <div className="setup-container">
        <h2>Welcome to AurSh Music</h2>
        <p>Please enter the absolute path to your music directory to get started.</p>
        <form onSubmit={onConfigSubmit}>
          <input 
            type="text" 
            placeholder="e.g. C:\Users\Cortez\Music" 
            value={configInput}
            onChange={e => setConfigInput(e.target.value)}
          />
          <button type="submit">Scan Directory</button>
        </form>
      </div>
    );
  }

  let displayedTracks = tracks;
  if (currentView === 'Search' && searchQuery) {
    const q = searchQuery.toLowerCase();
    displayedTracks = tracks.filter(t => t.title.toLowerCase().includes(q) || t.artist.toLowerCase().includes(q));
  } else if (currentView === 'Liked') {
    const likedIds = new Set(userData?.likedTracks || []);
    displayedTracks = tracks.filter(t => likedIds.has(t.id));
  } else if (currentView === 'Library') {
    return (
      <>
        <h2 className="section-title">Your Playlists</h2>
        <div className="grid">
          {userData?.playlists.map(p => (
            <div key={p.id} className="card">
              <div className="card-info" style={{marginTop: 0, padding: '16px'}}>
                <div className="title">{p.name}</div>
                <div className="artist">{p.trackIds.length} tracks</div>
              </div>
            </div>
          ))}
          {(!userData?.playlists || userData.playlists.length === 0) && (
            <p>You haven't created any playlists yet.</p>
          )}
        </div>
      </>
    );
  }

  return (
    <>
      {currentView === 'Search' && (
        <div style={{marginBottom: '20px'}}>
          <input 
            type="text" 
            placeholder="Search for songs or artists..." 
            value={searchQuery}
            onChange={e => setSearchQuery(e.target.value)}
            style={{padding: '10px', width: '100%', maxWidth: '400px', borderRadius: '20px', border: 'none', backgroundColor: '#333', color: 'white'}}
          />
        </div>
      )}
      <h2 className="section-title">
        {currentView === 'Home' ? 'Your Music' : currentView === 'Liked' ? 'Liked Songs' : 'Search Results'}
      </h2>
      <div className="grid">
        {displayedTracks.map(track => (
          <div key={track.id} className="card" onClick={() => playTrack(track)}>
            <div className="card-image-wrapper">
              <img 
                src={track.hasCover ? `/api/cover/${track.id}` : 'https://upload.wikimedia.org/wikipedia/commons/3/3c/No-album-art.png'} 
                alt={track.title} 
              />
              <button className="card-play-btn">▶</button>
            </div>
            <div className="card-info">
              <div className="title">{track.title}</div>
              <div className="artist">{track.artist}</div>
            </div>
          </div>
        ))}
      </div>
    </>
  );
};
