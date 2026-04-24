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

/** Ship as placed on the 10×10 grid — used by PlacementComponent and SignalRService */
export interface ShipPlacement {
  size: number;
  row: number;
  col: number;
  horizontal: boolean;
}

export type ShotResultType = 'miss' | 'hit' | 'sunk';

/** Row/Col are sea-absolute coordinates on the shared 50×50 sea. */
export interface BattleShotResult {
  shooterId: number;
  targetId: number;
  row: number;
  col: number;
  result: ShotResultType;
  sunkCells?: { row: number; col: number }[];
}

/** Sea-absolute top-left offset of a player's 10×10 fleet area. */
export interface FleetPosition {
  playerId: number;
  offsetRow: number;
  offsetCol: number;
}

export type FleetMoveDirection = 'up' | 'down' | 'left' | 'right';

