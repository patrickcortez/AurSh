import React from 'react';
import type { ViewState } from '../types';

interface SidebarProps {
  currentView: ViewState;
  setCurrentView: (view: ViewState) => void;
  onCreatePlaylist: () => void;
}

export const Sidebar: React.FC<SidebarProps> = ({ currentView, setCurrentView, onCreatePlaylist }) => {
  return (
    <div className="sidebar">
      <div className="logo">
        <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24">
           <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm4.6 14.6c-.2.3-.5.4-.8.2-2.1-1.3-4.8-1.6-7.9-.9-.3.1-.7-.1-.8-.4-.1-.3.1-.7.4-.8 3.4-.8 6.4-.4 8.8 1.1.4.1.5.5.3.8zm1.1-2.4c-.2.4-.6.5-.9.3-2.5-1.5-5.6-1.9-8.5-1-.4.1-.8-.1-1-.5-.1-.4.1-.8.5-1 3.3-1 6.8-.6 9.6 1.1.4.2.5.7.3 1.1zm.1-2.5c-3-1.8-8-2-10.8-1.1-.5.2-1-.1-1.1-.6-.2-.5.1-1 .6-1.1 3.2-1 8.8-.8 12.3 1.3.4.2.6.8.4 1.2-.2.5-.8.6-1.4.3z"/>
        </svg>
        AurSh Music
      </div>
      <div className="nav-items">
        <div className={`nav-item ${currentView === 'Home' ? 'active' : ''}`} onClick={() => setCurrentView('Home')}>
          <span>🏠</span> Home
        </div>
        <div className={`nav-item ${currentView === 'Search' ? 'active' : ''}`} onClick={() => setCurrentView('Search')}>
          <span>🔍</span> Search
        </div>
        <div className={`nav-item ${currentView === 'Library' ? 'active' : ''}`} onClick={() => setCurrentView('Library')}>
          <span>📚</span> Your Library
        </div>
      </div>
      <div className="nav-items" style={{marginTop: '24px'}}>
        <div className="nav-item" onClick={onCreatePlaylist}>
          <span>➕</span> Create Playlist
        </div>
        <div className={`nav-item ${currentView === 'Liked' ? 'active' : ''}`} onClick={() => setCurrentView('Liked')}>
          <span>❤️</span> Liked Songs
        </div>
      </div>
    </div>
  );
};
