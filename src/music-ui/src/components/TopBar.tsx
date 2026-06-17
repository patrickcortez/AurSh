import React from 'react';

interface TopBarProps {
  searchQuery: string;
  setSearchQuery: (val: string) => void;
  goHome: () => void;
}

export const TopBar: React.FC<TopBarProps> = ({ searchQuery, setSearchQuery, goHome }) => {
  return (
    <div className="top-bar">
      <div className="top-bar-left">
      </div>
      
      <div className="top-bar-center">
        <button className="circle-btn" style={{ background: '#282828', color: '#fff', cursor: 'pointer' }} onClick={goHome} title="Home">
          <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24"><path d="M12.5 3.247a1 1 0 00-1 0L4 7.577V20h4.5v-6a1 1 0 011-1h5a1 1 0 011 1v6H20V7.577l-7.5-4.33z"/></svg>
        </button>
        <div className="search-container">
          <svg viewBox="0 0 24 24" fill="currentColor" height="20" width="20" style={{ color: 'var(--text-secondary)' }}>
            <path d="M10.533 1.279c-5.18 0-9.407 4.14-9.407 9.279s4.226 9.279 9.407 9.279c2.234 0 4.29-.77 5.907-2.058l4.353 4.353a1 1 0 101.414-1.414l-4.344-4.344a9.157 9.157 0 002.077-5.816c0-5.14-4.226-9.279-9.407-9.279zm-7.407 9.279c0-4.006 3.302-7.279 7.407-7.279s7.407 3.273 7.407 7.279-3.302 7.279-7.407 7.279-7.407-3.273-7.407-7.279z"/>
          </svg>
          <input 
            type="text" 
            placeholder="What do you want to play?" 
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
          <svg viewBox="0 0 24 24" fill="currentColor" height="20" width="20" style={{ color: 'var(--text-secondary)', marginLeft: '8px' }}>
            <path d="M15 15.5c0 1.104-.896 2-2 2s-2-.896-2-2v-7c0-1.104.896-2 2-2s2 .896 2 2v7zm-2 4c-1.657 0-3-1.343-3-3v-7c0-1.657 1.343-3 3-3s3 1.343 3 3v7c0 1.657-1.343 3-3 3z"/>
          </svg>
        </div>
      </div>
      
      <div className="top-bar-right">
      </div>
    </div>
  );
};
