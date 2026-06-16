import React from 'react';

export const TopBar: React.FC = () => {
  return (
    <div className="top-bar">
      <div className="top-bar-left">
        <button className="circle-btn" style={{ background: 'transparent' }}>
          <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24">
            <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm4.6 14.6c-.2.3-.5.4-.8.2-2.1-1.3-4.8-1.6-7.9-.9-.3.1-.7-.1-.8-.4-.1-.3.1-.7.4-.8 3.4-.8 6.4-.4 8.8 1.1.4.1.5.5.3.8zm1.1-2.4c-.2.4-.6.5-.9.3-2.5-1.5-5.6-1.9-8.5-1-.4.1-.8-.1-1-.5-.1-.4.1-.8.5-1 3.3-1 6.8-.6 9.6 1.1.4.2.5.7.3 1.1zm.1-2.5c-3-1.8-8-2-10.8-1.1-.5.2-1-.1-1.1-.6-.2-.5.1-1 .6-1.1 3.2-1 8.8-.8 12.3 1.3.4.2.6.8.4 1.2-.2.5-.8.6-1.4.3z"/>
          </svg>
        </button>
        <button className="circle-btn">
          <svg viewBox="0 0 24 24" fill="currentColor" height="16" width="16"><path d="M15.54 21.15L5.095 12.23 15.54 3.31l1.06 1.182L7.509 12.23l9.091 7.738-1.06 1.182z"/></svg>
        </button>
        <button className="circle-btn">
          <svg viewBox="0 0 24 24" fill="currentColor" height="16" width="16"><path d="M7.96 21.15l-.426-.576L17.509 12 7.534 3.426l.426-.576L18.491 12 7.96 21.15z"/></svg>
        </button>
      </div>
      
      <div className="top-bar-center">
        <button className="circle-btn" style={{ background: '#282828', color: '#fff' }}>
          <svg viewBox="0 0 24 24" fill="currentColor" height="24" width="24"><path d="M12.5 3.247a1 1 0 00-1 0L4 7.577V20h4.5v-6a1 1 0 011-1h5a1 1 0 011 1v6H20V7.577l-7.5-4.33z"/></svg>
        </button>
        <div className="search-container">
          <svg viewBox="0 0 24 24" fill="currentColor" height="20" width="20" style={{ color: 'var(--text-secondary)' }}>
            <path d="M10.533 1.279c-5.18 0-9.407 4.14-9.407 9.279s4.226 9.279 9.407 9.279c2.234 0 4.29-.77 5.907-2.058l4.353 4.353a1 1 0 101.414-1.414l-4.344-4.344a9.157 9.157 0 002.077-5.816c0-5.14-4.226-9.279-9.407-9.279zm-7.407 9.279c0-4.006 3.302-7.279 7.407-7.279s7.407 3.273 7.407 7.279-3.302 7.279-7.407 7.279-7.407-3.273-7.407-7.279z"/>
          </svg>
          <input type="text" placeholder="What do you want to play?" />
          <svg viewBox="0 0 24 24" fill="currentColor" height="20" width="20" style={{ color: 'var(--text-secondary)', marginLeft: '8px' }}>
            <path d="M15 15.5c0 1.104-.896 2-2 2s-2-.896-2-2v-7c0-1.104.896-2 2-2s2 .896 2 2v7zm-2 4c-1.657 0-3-1.343-3-3v-7c0-1.657 1.343-3 3-3s3 1.343 3 3v7c0 1.657-1.343 3-3 3z"/>
          </svg>
        </div>
      </div>
      
      <div className="top-bar-right">
        <button className="circle-btn" style={{ background: 'transparent' }}>
          <svg viewBox="0 0 24 24" fill="currentColor" height="16" width="16"><path d="M12 3a7 7 0 00-7 7v4.542l-2.718 3.624A1 1 0 003.082 19H8.5a3.5 3.5 0 107 0h5.418a1 1 0 00.8-1.834L19 13.542V10a7 7 0 00-7-7zM8.5 19h7a1.5 1.5 0 11-7 0zm10-5.458L20.282 17H3.718L5.5 14.624V10a5 5 0 1110 0v3.542z"/></svg>
        </button>
        <button className="circle-btn" style={{ background: 'transparent' }}>
          <svg viewBox="0 0 24 24" fill="currentColor" height="16" width="16"><path d="M10 20.5A2.5 2.5 0 017.5 18H10v2.5zm0-2.5h-5a2.5 2.5 0 115 0zm2-8.5a4 4 0 118 0 4 4 0 01-8 0zm4-2a2 2 0 100 4 2 2 0 000-4zm1.5 11c-.538 0-1.042-.164-1.46-.445.698-.795 1.134-1.848 1.252-2.993A4.5 4.5 0 0019 17.5a2.5 2.5 0 11-2.5 2.5V20c1.38 0 2.5-1.12 2.5-2.5s-1.12-2.5-2.5-2.5-2.5 1.12-2.5 2.5v.5h-1v-.5a3.5 3.5 0 117 0c0 1.933-1.567 3.5-3.5 3.5z"/></svg>
        </button>
        <button className="circle-btn">
          {/* User Avatar Placeholder */}
          <div style={{ width: 24, height: 24, borderRadius: '50%', backgroundColor: '#535353' }}></div>
        </button>
        {/* Window controls mockup */}
        <div style={{ display: 'flex', gap: '8px', marginLeft: '16px', opacity: 0.7 }}>
          <span style={{ fontSize: '12px' }}>—</span>
          <span style={{ fontSize: '12px' }}>□</span>
          <span style={{ fontSize: '12px' }}>×</span>
        </div>
      </div>
    </div>
  );
};
