import React from 'react';
import type { Track } from '../types';

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
  togglePlay, playNext, playPrev, toggleShuffle, toggleRepeat, toggleLike,
  handleSeek, handleVolume, formatTime
}) => {
  return (
    <div className="player-bar">
      <div className="now-playing">
        {currentTrack ? (
          <>
            <img 
              src={currentTrack.hasCover ? `/api/cover/${currentTrack.id}` : 'https://upload.wikimedia.org/wikipedia/commons/3/3c/No-album-art.png'} 
              alt={currentTrack.title} 
            />
            <div className="now-playing-info">
              <div className="title">{currentTrack.title}</div>
              <div className="artist">{currentTrack.artist}</div>
            </div>
            <button className="like-btn" onClick={toggleLike} style={{color: isLiked ? '#1db954' : 'inherit'}}>
              {isLiked ? '❤️' : '🤍'}
            </button>
          </>
        ) : (
          <div className="now-playing-empty"></div>
        )}
      </div>

      <div className="controls-container">
        <div className="controls">
          <button className="small-btn" onClick={toggleShuffle} style={{color: isShuffle ? '#1db954' : 'inherit'}}>🔀</button>
          <button onClick={playPrev}>⏮</button>
          <button className="play-btn" onClick={togglePlay}>
            {isPlaying ? '⏸' : '▶'}
          </button>
          <button onClick={playNext}>⏭</button>
          <button className="small-btn" onClick={toggleRepeat} style={{color: isRepeat ? '#1db954' : 'inherit'}}>🔁</button>
        </div>
        <div className="progress-bar">
          <span className="progress-time">{formatTime(progress)}</span>
          <input 
            type="range" 
            className="slider" 
            min={0} 
            max={currentTrack?.duration || 100} 
            value={progress}
            onChange={handleSeek}
            disabled={!currentTrack}
          />
          <span className="progress-time">
            {formatTime(currentTrack?.duration || 0)}
          </span>
        </div>
      </div>

      <div className="volume-container">
        <span className="vol-icon">🔊</span>
        <input 
          type="range" 
          className="slider" 
          min={0} 
          max={1} 
          step={0.01} 
          value={volume} 
          onChange={handleVolume} 
        />
      </div>
    </div>
  );
};
