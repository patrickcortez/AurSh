export interface Track {
  id: string;
  title: string;
  artist: string;
  album: string;
  year: number;
  genre: string;
  trackNumber: number;
  bitrate: number;
  sampleRate: number;
  channels: number;
  duration: number;
  path: string;
  hasCover: boolean;
}

export interface Status {
  configured: boolean;
  directory: string;
  trackCount: number;
}

export interface Playlist {
  id: string;
  name: string;
  trackIds: string[];
}

export interface UserData {
  likedTracks: string[];
  playlists: Playlist[];
}

export type ViewState = 'Home' | 'Search' | 'Library' | 'Liked';
