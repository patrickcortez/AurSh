import React, { useState, useEffect } from 'react';

interface SettingsModalProps {
  isOpen: boolean;
  onClose: () => void;
  refreshTracks: () => void;
}

export const SettingsModal: React.FC<SettingsModalProps> = ({ isOpen, onClose, refreshTracks }) => {
  const [directory, setDirectory] = useState("");
  const [status, setStatus] = useState<any>(null);

  useEffect(() => {
    if (isOpen) {
      fetch('/api/status')
        .then(res => res.json())
        .then(data => {
          setStatus(data);
          setDirectory(data.directory);
        })
        .catch(console.error);
    }
  }, [isOpen]);

  if (!isOpen) return null;

  const handleSave = async () => {
    try {
      await fetch('/api/config', {
        method: 'POST',
        body: directory
      });
      refreshTracks();
      onClose();
    } catch (e) {
      console.error(e);
    }
  };

  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', zIndex: 3000,
      display: 'flex', alignItems: 'center', justifyContent: 'center'
    }}>
      <div style={{
        background: '#282828', borderRadius: '8px', padding: '24px', width: '400px',
        boxShadow: '0 4px 60px rgba(0,0,0,.5)', display: 'flex', flexDirection: 'column'
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
          <h2 style={{ margin: 0, fontSize: '24px' }}>Settings</h2>
          <button onClick={onClose} style={{ background: 'transparent', border: 'none', color: 'var(--text-secondary)', cursor: 'pointer' }}>
            <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M1.47 1.47a.75.75 0 011.06 0L8 6.94l5.47-5.47a.75.75 0 111.06 1.06L9.06 8l5.47 5.47a.75.75 0 11-1.06 1.06L8 9.06l-5.47 5.47a.75.75 0 01-1.06-1.06L6.94 8 1.47 2.53a.75.75 0 010-1.06z"/></svg>
          </button>
        </div>

        <div style={{ marginBottom: '16px' }}>
          <label style={{ display: 'block', fontSize: '14px', marginBottom: '8px', fontWeight: 'bold' }}>Music Directory</label>
          <input 
            value={directory} 
            onChange={(e) => setDirectory(e.target.value)}
            placeholder="/path/to/music"
            style={{ background: '#3E3E3E', color: '#fff', border: 'none', borderRadius: '4px', padding: '12px', fontSize: '14px', width: '100%', boxSizing: 'border-box' }}
          />
        </div>
        
        {status && (
          <div style={{ fontSize: '12px', color: 'var(--text-secondary)', marginBottom: '24px' }}>
            Tracks found: {status.trackCount}
          </div>
        )}

        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <button 
            onClick={handleSave} 
            style={{ background: '#fff', color: '#000', border: 'none', padding: '10px 24px', borderRadius: '500px', fontWeight: 'bold', cursor: 'pointer' }}
          >
            Save & Scan
          </button>
        </div>
      </div>
    </div>
  );
};
