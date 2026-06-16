import React from 'react';
import type { Track } from '../types';
import { FALLBACK_COVER } from '../constants';

interface PlayerBarProps {
  currentTrack: Track | null;
  isPlaying: boolean;
  progress: number;
  volume: number;
  isShuffle: boolean;
  isRepeat: boolean;
  isLiked: boolean;
  togglePlay: () => void;
  playNext: () => void;
  playPrev: () => void;
  toggleShuffle: () => void;
  toggleRepeat: () => void;
  toggleLike: () => void;
  handleSeek: (e: React.ChangeEvent<HTMLInputElement>) => void;
  handleVolume: (e: React.ChangeEvent<HTMLInputElement>) => void;
  formatTime: (seconds: number) => string;
}

export const PlayerBar: React.FC<PlayerBarProps> = ({
  currentTrack, isPlaying, progress, volume, isShuffle, isRepeat, isLiked,
  togglePlay, playNext, playPrev, toggleShuffle, toggleRepeat, toggleLike, handleSeek, handleVolume, formatTime
}) => {
  return (
    <div className="player-bar">
      <div className="player-left">
        {currentTrack && (
          <>
            <img 
              src={`/api/cover/${currentTrack.id}`} 
              alt="cover" 
              onError={(e) => { e.currentTarget.src = FALLBACK_COVER }}
            />
            <div className="player-info">
              <span className="title">{currentTrack.title}</span>
              <span className="artist">{currentTrack.artist}</span>
            </div>
            <button className="like-btn" onClick={toggleLike} style={{ color: isLiked ? 'var(--accent)' : 'var(--text-secondary)' }}>
              {isLiked ? '💚' : <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M1.624 3.018a4.249 4.249 0 015.65-.426l.725.592.726-.592a4.25 4.25 0 015.65.426 4.333 4.333 0 01.127 6.002L8 15.5 1.498 9.02a4.333 4.333 0 01.126-6.002z"/></svg>}
            </button>
          </>
        )}
      </div>

      <div className="player-center">
        <div className="player-controls">
          <button style={{ color: isShuffle ? 'var(--accent)' : 'var(--text-secondary)' }} onClick={toggleShuffle}>
             <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M13.151.922a.75.75 0 10-1.06 1.06L13.109 3H11.16a3.75 3.75 0 00-2.873 1.34l-6.173 7.356A2.25 2.25 0 01.39 12.5H0V14h.391a3.75 3.75 0 002.873-1.34l6.173-7.356a2.25 2.25 0 011.724-.804h1.947l-1.017 1.018a.75.75 0 001.06 1.06L15.98 3.75 13.15.922zM.391 3.5H0V2h.391c1.109 0 2.16.49 2.873 1.34L4.89 5.277l-.979 1.167-1.796-2.14A2.25 2.25 0 00.39 3.5z"/><path d="M7.5 10.723l.98-1.167 1.795 2.14A2.25 2.25 0 0011.999 12h1.897l-1.017-1.018a.75.75 0 111.06-1.06l2.829 2.828-2.829 2.828a.75.75 0 11-1.06-1.06L13.896 13.5H12a3.75 3.75 0 01-2.873-1.34l-1.627-1.937z"/></svg>
          </button>
          <button onClick={playPrev}>
            <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M3.3 1a.7.7 0 01.7.7v5.15l9.95-5.744a.7.7 0 011.05.606v12.575a.7.7 0 01-1.05.607L4 9.149V14.3a.7.7 0 01-1.4 0V1.7a.7.7 0 01.7-.7z"/></svg>
          </button>
          <button className="play-pause-btn" onClick={togglePlay}>
            {isPlaying ? (
              <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M2.7 1a.7.7 0 00-.7.7v12.6a.7.7 0 00.7.7h2.6a.7.7 0 00.7-.7V1.7a.7.7 0 00-.7-.7H2.7zm8 0a.7.7 0 00-.7.7v12.6a.7.7 0 00.7.7h2.6a.7.7 0 00.7-.7V1.7a.7.7 0 00-.7-.7h-2.6z"/></svg>
            ) : (
              <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M3 1.713a.7.7 0 011.05-.607l10.89 6.288a.7.7 0 010 1.212L4.05 14.894A.7.7 0 013 14.288V1.713z"/></svg>
            )}
          </button>
          <button onClick={playNext}>
            <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M12.7 1a.7.7 0 00-.7.7v5.15L2.05 1.107A.7.7 0 001 1.712v12.575a.7.7 0 001.05.607L12 9.149V14.3a.7.7 0 001.4 0V1.7a.7.7 0 00-.7-.7z"/></svg>
          </button>
          <button style={{ color: isRepeat ? 'var(--accent)' : 'var(--text-secondary)' }} onClick={toggleRepeat}>
            <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M0 4.75A3.75 3.75 0 013.75 1h8.5A3.75 3.75 0 0116 4.75v5a3.75 3.75 0 01-3.75 3.75H9.81l1.018 1.018a.75.75 0 11-1.06 1.06L6.939 12.75l2.829-2.828a.75.75 0 111.06 1.06L9.811 12h2.439a2.25 2.25 0 002.25-2.25v-5a2.25 2.25 0 00-2.25-2.25h-8.5A2.25 2.25 0 001.5 4.75v5A2.25 2.25 0 003.75 12H5v1.5H3.75A3.75 3.75 0 010 9.75v-5z"/></svg>
          </button>
        </div>

        <div className="progress-container">
          <span className="progress-time">{formatTime(progress)}</span>
          <input 
            type="range" 
            className="custom-slider" 
            min="0" 
            max={currentTrack?.duration || 100} 
            value={progress} 
            onChange={handleSeek} 
            style={{ '--value': `${currentTrack?.duration ? (progress / currentTrack.duration) * 100 : 0}%` } as React.CSSProperties}
          />
          <span className="progress-time">{formatTime(currentTrack?.duration || 0)}</span>
        </div>
      </div>

      <div className="player-right">
        <button style={{ background: 'none', border: 'none', color: 'var(--text-secondary)' }}>
          <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M15 15H1v-1.5h14V15zm0-4.5H1V9h14v1.5zm-14-7A2.5 2.5 0 013.5 1h9a2.5 2.5 0 010 5h-9A2.5 2.5 0 011 3.5zm2.5-1a1 1 0 000 2h9a1 1 0 100-2h-9z"/></svg>
        </button>
        <button style={{ background: 'none', border: 'none', color: 'var(--text-secondary)' }}>
          <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16"><path d="M6 2.75C6 1.784 6.784 1 7.75 1h6.5c.966 0 1.75.784 1.75 1.75v10.5A1.75 1.75 0 0114.25 15h-6.5A1.75 1.75 0 016 13.25V2.75zm1.75-.25a.25.25 0 00-.25.25v10.5c0 .138.112.25.25.25h6.5a.25.25 0 00.25-.25V2.75a.25.25 0 00-.25-.25h-6.5zm-6 0a.25.25 0 00-.25.25v6.5c0 .138.112.25.25.25H4V11H1.75A1.75 1.75 0 010 9.25v-6.5C0 1.784.784 1 1.75 1H4v1.5H1.75zM4 15H2v-1.5h2V15z"/></svg>
        </button>
        
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', width: '100px' }}>
          <svg viewBox="0 0 16 16" fill="currentColor" height="16" width="16" style={{ color: 'var(--text-secondary)' }}><path d="M9.741.85a.75.75 0 01.375.65v13a.75.75 0 01-1.125.65l-6.925-4a3.642 3.642 0 01-1.33-4.967 3.639 3.639 0 011.33-1.332l6.925-4a.75.75 0 01.75 0zm-6.924 5.3a2.139 2.139 0 000 3.7l5.8 3.35V2.8l-5.8 3.35zm8.683 4.29V5.56a2.75 2.75 0 010 4.88z"/></svg>
          <input 
            type="range" 
            className="custom-slider" 
            min="0" 
            max="1" 
            step="0.01" 
            value={volume} 
            onChange={handleVolume} 
            style={{ '--value': `${volume * 100}%` } as React.CSSProperties}
          />
        </div>
      </div>
    </div>
  );
};
