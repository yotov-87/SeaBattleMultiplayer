export interface Player {
  id: number;
  username: string;
  isOnline: boolean;
  isFriend: boolean;
}

export interface GameInvite {
  senderId: number;
  senderUsername: string;
}
