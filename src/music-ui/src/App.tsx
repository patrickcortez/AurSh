import React, { useEffect, useState, useRef } from 'react';
import './App.css';
import type { Track, Status, UserData, ViewState } from './types';
import { Sidebar } from './components/Sidebar';
import { MainView } from './components/MainView';
import { PlayerBar } from './components/PlayerBar';

function App() {
  const [tracks, setTracks] = useState<Track[]>([]);
  const [status, setStatus] = useState<Status | null>(null);
  const [userData, setUserData] = useState<UserData | null>(null);
  const [configInput, setConfigInput] = useState('');
  
  const [currentView, setCurrentView] = useState<ViewState>('Home');
  const [currentTrack, setCurrentTrack] = useState<Track | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [progress, setProgress] = useState(0);
  const [volume, setVolume] = useState(1);
  const [isShuffle, setIsShuffle] = useState(false);
  const [isRepeat, setIsRepeat] = useState(false);
  
  const audioRef = useRef<HTMLAudioElement | null>(null);

  const fetchStatusAndTracks = async () => {
    try {
      const statusRes = await fetch('/api/status');
      const statusData = await statusRes.json();
      setStatus(statusData);

      if (statusData.configured && statusData.trackCount > 0) {
        const tracksRes = await fetch('/api/tracks');
        const tracksData = await tracksRes.json();
        setTracks(tracksData);
      }

      const userRes = await fetch('/api/userdata');
      const userObj = await userRes.json();
      setUserData(userObj);
    } catch (err) {
      console.error('Failed to fetch data:', err);
    }
  };

  useEffect(() => {
    fetchStatusAndTracks();
    const interval = setInterval(fetchStatusAndTracks, 5000);
    return () => clearInterval(interval);
  }, []);

  const handleConfigSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await fetch('/api/config', {
        method: 'POST',
        body: configInput
      });
      fetchStatusAndTracks();
    } catch (err) {
      console.error('Failed to set config:', err);
    }
  };

  const handleCreatePlaylist = async () => {
    const name = window.prompt('Enter playlist name:');
    if (name) {
      try {
        const res = await fetch('/api/playlist', { method: 'POST', body: name });
        const newUserData = await res.json();
        setUserData(newUserData);
      } catch (err) {
        console.error('Failed to create playlist:', err);
      }
    }
  };

  const toggleLike = async () => {
    if (!currentTrack) return;
    try {
      const res = await fetch(`/api/like/${currentTrack.id}`, { method: 'POST' });
      const newUserData = await res.json();
      setUserData(newUserData);
    } catch (err) {
      console.error('Failed to toggle like:', err);
    }
  };

  const playTrack = (track: Track) => {
    setCurrentTrack(track);
    setIsPlaying(true);
    if (audioRef.current) {
      audioRef.current.src = `/api/stream/${track.id}`;
      audioRef.current.play();
    }
  };

  const togglePlay = () => {
    if (audioRef.current) {
      if (isPlaying) {
        audioRef.current.pause();
      } else {
        audioRef.current.play();
      }
      setIsPlaying(!isPlaying);
    }
  };

  const playNext = () => {
    if (tracks.length === 0) return;
    if (isShuffle) {
      const randomIdx = Math.floor(Math.random() * tracks.length);
      playTrack(tracks[randomIdx]);
      return;
    }
    
    if (!currentTrack) {
      playTrack(tracks[0]);
      return;
    }
    
    const idx = tracks.findIndex(t => t.id === currentTrack.id);
    const nextIdx = (idx + 1) % tracks.length;
    playTrack(tracks[nextIdx]);
  };

  const playPrev = () => {
    if (tracks.length === 0) return;
    if (!currentTrack) {
      playTrack(tracks[0]);
      return;
    }
    const idx = tracks.findIndex(t => t.id === currentTrack.id);
    const prevIdx = idx <= 0 ? tracks.length - 1 : idx - 1;
    playTrack(tracks[prevIdx]);
  };

  const onAudioEnded = () => {
    if (isRepeat && audioRef.current && currentTrack) {
      audioRef.current.currentTime = 0;
      audioRef.current.play();
    } else {
      playNext();
    }
  };

  const handleTimeUpdate = () => {
    if (audioRef.current) {
      setProgress(audioRef.current.currentTime);
    }
  };

  const handleSeek = (e: React.ChangeEvent<HTMLInputElement>) => {
    const time = Number(e.target.value);
    if (audioRef.current) {
      audioRef.current.currentTime = time;
      setProgress(time);
    }
  };

  const handleVolume = (e: React.ChangeEvent<HTMLInputElement>) => {
    const vol = Number(e.target.value);
    if (audioRef.current) {
      audioRef.current.volume = vol;
      setVolume(vol);
    }
  };

  const formatTime = (seconds: number) => {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}:${s.toString().padStart(2, '0')}`;
  };

  const isLiked = currentTrack ? (userData?.likedTracks.includes(currentTrack.id) || false) : false;

  return (
    <div className="layout">
      <Sidebar 
        currentView={currentView} 
        setCurrentView={setCurrentView} 
        onCreatePlaylist={handleCreatePlaylist} 
      />

      <div className="main-area">
        <div className="topbar">
          <div className="nav-arrows">
            <button>&lt;</button>
            <button>&gt;</button>
          </div>
          <div className="profile">User</div>
        </div>
        
        <div className="content-scroll">
          <div className="header-bg"></div>
          <div className="content-inner">
            <MainView 
              status={status}
              tracks={tracks}
              userData={userData}
              currentView={currentView}
              onConfigSubmit={handleConfigSubmit}
              configInput={configInput}
              setConfigInput={setConfigInput}
              playTrack={playTrack}
            />
          </div>
        </div>
      </div>

      <PlayerBar 
        currentTrack={currentTrack}
        isPlaying={isPlaying}
        progress={progress}
        volume={volume}
        isShuffle={isShuffle}
        isRepeat={isRepeat}
        isLiked={isLiked}
        togglePlay={togglePlay}
        playNext={playNext}
        playPrev={playPrev}
        toggleShuffle={() => setIsShuffle(!isShuffle)}
        toggleRepeat={() => setIsRepeat(!isRepeat)}
        toggleLike={toggleLike}
        handleSeek={handleSeek}
        handleVolume={handleVolume}
        formatTime={formatTime}
      />

      <audio 
        ref={audioRef} 
        onTimeUpdate={handleTimeUpdate} 
        onEnded={onAudioEnded} 
      />
    </div>
  );
}

export default App;
