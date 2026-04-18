export interface Player {
  id: number;
  username: string;
  isOnline: boolean;
  isFriend: boolean;
}

export interface GameInvite {
  senderId: number;
  senderUsername: string;
  roomId: string;
}

export interface LobbyMember {
  id: number;
  username: string;
}

export interface ChatMessage {
  senderId: number;
  senderUsername: string;
  text: string;
  timestamp: Date;
}
