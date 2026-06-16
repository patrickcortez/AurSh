export interface Track {
  id: string;
  title: string;
  artist: string;
  album: string;
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
