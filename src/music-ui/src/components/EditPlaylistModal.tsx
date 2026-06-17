import React, { useState, useRef, useEffect } from 'react';
import { FALLBACK_COVER } from '../constants';
import type { Playlist } from '../types';

interface EditPlaylistModalProps {
  playlist: Playlist;
  isOpen: boolean;
  onClose: () => void;
  onSave: () => void;
}

export const EditPlaylistModal: React.FC<EditPlaylistModalProps> = ({ playlist, isOpen, onClose, onSave }) => {
  const [name, setName] = useState(playlist.name);
  const [desc, setDesc] = useState(playlist.description || "");
  const [coverArt, setCoverArt] = useState(playlist.coverArt || "");
  const [bannerArt, setBannerArt] = useState(playlist.bannerArt || "");
  const coverInputRef = useRef<HTMLInputElement>(null);
  const bannerInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (isOpen) {
      setName(playlist.name);
      setDesc(playlist.description || "");
      setCoverArt(playlist.coverArt || "");
      setBannerArt(playlist.bannerArt || "");
    }
  }, [isOpen, playlist]);

  if (!isOpen) return null;

  const resizeImage = (file: File, maxWidth: number, maxHeight: number, callback: (base64: string) => void) => {
    const reader = new FileReader();
    reader.onload = (ev) => {
      if (!ev.target?.result) return;
      const img = new Image();
      img.onload = () => {
        let { width, height } = img;
        if (width > maxWidth || height > maxHeight) {
          const ratio = Math.min(maxWidth / width, maxHeight / height);
          width = Math.floor(width * ratio);
          height = Math.floor(height * ratio);
        }
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d');
        if (ctx) {
          ctx.drawImage(img, 0, 0, width, height);
          callback(canvas.toDataURL('image/jpeg', 0.85)); // Use jpeg for smaller size
        } else {
          callback(ev.target?.result as string); // fallback
        }
      };
      img.src = ev.target.result as string;
    };
    reader.readAsDataURL(file);
  };

  const handleSave = async () => {
    try {
      const res = await fetch(`/api/playlist/${playlist.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, description: desc, coverArt, bannerArt })
      });
      if (!res.ok) {
        alert("Failed to save playlist. The image might be too large.");
        return;
      }
      onSave();
      onClose();
    } catch (e) {
      console.error(e);
      alert("Network error occurred while saving.");
    }
  };

  const handleCoverUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    resizeImage(file, 512, 512, setCoverArt);
  };

  const handleBannerUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    resizeImage(file, 1920, 1080, setBannerArt);
  };

  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', zIndex: 2000,
      display: 'flex', alignItems: 'center', justifyContent: 'center'
    }}>
      <div style={{
        background: '#282828', borderRadius: '8px', padding: '24px', width: '524px',
        boxShadow: '0 4px 60px rgba(0,0,0,.5)', display: 'flex', flexDirection: 'column'
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
          <h2 style={{ margin: 0, fontSize: '24px' }}>Edit details</h2>
          <button onClick={onClose} style={{ background: 'transparent', border: 'none', color: 'var(--text-secondary)', cursor: 'pointer' }}>
            <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M1.47 1.47a.75.75 0 011.06 0L8 6.94l5.47-5.47a.75.75 0 111.06 1.06L9.06 8l5.47 5.47a.75.75 0 11-1.06 1.06L8 9.06l-5.47 5.47a.75.75 0 01-1.06-1.06L6.94 8 1.47 2.53a.75.75 0 010-1.06z"/></svg>
          </button>
        </div>

        <div 
          style={{ width: '100%', height: '120px', cursor: 'pointer', position: 'relative', background: '#3E3E3E', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '4px', overflow: 'hidden', marginBottom: '16px' }}
          onClick={() => bannerInputRef.current?.click()}
        >
          {bannerArt ? (
            <img src={bannerArt} style={{ width: '100%', height: '100%', objectFit: 'cover' }} alt="Playlist Banner" />
          ) : (
            <span style={{ color: 'var(--text-secondary)' }}>Add an optional background banner</span>
          )}
          <div style={{ position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#fff', fontSize: '14px', flexDirection: 'column', opacity: 0, transition: 'opacity 0.2s' }} onMouseEnter={e => e.currentTarget.style.opacity = '1'} onMouseLeave={e => e.currentTarget.style.opacity = '0'}>
            <svg viewBox="0 0 24 24" fill="currentColor" height="32" width="32" style={{ marginBottom: '8px' }}><path d="M17.39 5.86l1.75 1.75L6 20.75H4.25V19l13.14-13.14zm0-2.83a2.5 2.5 0 00-1.77.73L2.5 16.88V23h6.12L21.74 9.88a2.5 2.5 0 000-3.53l-1.77-1.77a2.5 2.5 0 00-2.58-.49z"/></svg>
            Choose banner
          </div>
          <input type="file" accept="image/*" style={{ display: 'none' }} ref={bannerInputRef} onChange={handleBannerUpload} />
        </div>

        <div style={{ display: 'flex', gap: '16px' }}>
          <div 
            style={{ width: '180px', height: '180px', cursor: 'pointer', position: 'relative', background: '#3E3E3E', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '4px', overflow: 'hidden', flexShrink: 0 }}
            onClick={() => coverInputRef.current?.click()}
          >
            <img 
              src={coverArt || FALLBACK_COVER} 
              style={{ width: '100%', height: '100%', objectFit: 'cover' }}
              alt="Playlist Cover" 
            />
            <div style={{ position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#fff', fontSize: '14px', flexDirection: 'column', opacity: 0, transition: 'opacity 0.2s' }} onMouseEnter={e => e.currentTarget.style.opacity = '1'} onMouseLeave={e => e.currentTarget.style.opacity = '0'}>
              <svg viewBox="0 0 24 24" fill="currentColor" height="48" width="48" style={{ marginBottom: '8px' }}><path d="M17.39 5.86l1.75 1.75L6 20.75H4.25V19l13.14-13.14zm0-2.83a2.5 2.5 0 00-1.77.73L2.5 16.88V23h6.12L21.74 9.88a2.5 2.5 0 000-3.53l-1.77-1.77a2.5 2.5 0 00-2.58-.49z"/></svg>
              Choose cover
            </div>
            <input type="file" accept="image/*" style={{ display: 'none' }} ref={coverInputRef} onChange={handleCoverUpload} />
          </div>

          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '16px' }}>
            <input 
              value={name} 
              onChange={(e) => setName(e.target.value)}
              placeholder="Name"
              style={{ background: '#3E3E3E', color: '#fff', border: 'none', borderRadius: '4px', padding: '12px', fontSize: '14px', width: '100%' }}
              autoFocus
            />
            <textarea 
              value={desc} 
              onChange={(e) => setDesc(e.target.value)}
              placeholder="Add an optional description"
              style={{ background: '#3E3E3E', color: '#fff', border: 'none', borderRadius: '4px', padding: '12px', fontSize: '14px', width: '100%', resize: 'none', flex: 1 }}
            />
          </div>
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '24px' }}>
          <button 
            onClick={handleSave} 
            style={{ background: '#fff', color: '#000', border: 'none', padding: '12px 32px', borderRadius: '500px', fontWeight: 'bold', cursor: 'pointer' }}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
};
