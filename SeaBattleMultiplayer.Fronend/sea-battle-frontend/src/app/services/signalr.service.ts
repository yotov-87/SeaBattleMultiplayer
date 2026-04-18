import { Injectable, inject, signal, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import { GameInvite, LobbyMember, ChatMessage, BattleShotResult, ShipPlacement } from '../models/player.models';

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private readonly authService = inject(AuthService);

  private hub: signalR.HubConnection | null = null;

  // в”Ђв”Ђ Presence в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
  readonly onlineUserIds = signal<Set<number>>(new Set());
  readonly latestInvite = signal<GameInvite | null>(null);
  readonly userJoined$ = new Subject<{ id: number; username: string }>();

  // в”Ђв”Ђ Lobby в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
  readonly lobbyRoomId = signal<string | null>(null);
  readonly lobbyMembers = signal<LobbyMember[]>([]);
  readonly chatMessages = signal<ChatMessage[]>([]);
  readonly isHost = signal<boolean>(false);

  private readonly roomCreated$ = new Subject<string>();

  // в”Ђв”Ђ Game phase в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
  readonly gameStarted$ = new Subject<void>();
  readonly allReady$ = new Subject<void>();

  // в”Ђв”Ђ Battle в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
  readonly myFleet = signal<ShipPlacement[]>([]);
  readonly currentTurnPlayerId = signal<number | null>(null);
  readonly currentTurnUsername = signal<string>('');
  readonly shotResults = signal<BattleShotResult[]>([]);
  readonly eliminatedPlayerIds = signal<Set<number>>(new Set());
  readonly battleWinnerId = signal<number | null>(null);
  readonly battleWinnerUsername = signal<string | null>(null);
  readonly battleChatMessages = signal<ChatMessage[]>([]);

  // в”Ђв”Ђ Connection в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

  startConnection(): Promise<void> {
    if (this.hub?.state === signalR.HubConnectionState.Connected) {
      return Promise.resolve();
    }

    this.hub = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.hubUrl}/hubs/game`, {
        accessTokenFactory: () => this.authService.getToken() ?? ''
      })
      .withAutomaticReconnect()
      .build();

    // Presence
    this.hub.on('OnlineUsers', (ids: number[]) => {
      this.onlineUserIds.set(new Set(ids));
    });

    this.hub.on('UserOnline', (userId: number, username: string) => {
      this.onlineUserIds.update(set => new Set([...set, userId]));
      this.userJoined$.next({ id: userId, username });
    });

    this.hub.on('UserOffline', (userId: number) => {
      this.onlineUserIds.update(set => {
        const next = new Set(set);
        next.delete(userId);
        return next;
      });
    });

    this.hub.on('GameInviteReceived', (senderId: number, senderUsername: string, roomId: string) => {
      this.latestInvite.set({ senderId, senderUsername, roomId });
    });

    // Lobby
    this.hub.on('RoomCreated', (roomId: string) => {
      this.lobbyRoomId.set(roomId);
      this.chatMessages.set([]);
      this.isHost.set(true);
      this.roomCreated$.next(roomId);
    });

    this.hub.on('RoomJoined', (roomId: string) => {
      this.lobbyRoomId.set(roomId);
      this.chatMessages.set([]);
      this.isHost.set(false);
    });

    this.hub.on('LobbyState', (members: LobbyMember[]) => {
      this.lobbyMembers.set(members);
    });

    this.hub.on('UserJoinedLobby', (userId: number, username: string) => {
      this.lobbyMembers.update(m =>
        m.some(x => x.id === userId) ? m : [...m, { id: userId, username }]
      );
    });

    this.hub.on('UserLeftLobby', (userId: number) => {
      this.lobbyMembers.update(m => m.filter(x => x.id !== userId));
    });

    this.hub.on('LobbyChatMessage', (senderId: number, senderUsername: string, text: string, timestamp: string) => {
      this.chatMessages.update(msgs => [
        ...msgs,
        { senderId, senderUsername, text, timestamp: new Date(timestamp) }
      ]);
    });

    this.hub.on('InviteDeclined', (username: string) => {
      console.info(`${username} declined the invite.`);
    });

    this.hub.on('InviteError', (msg: string) => {
      console.warn('Invite error:', msg);
    });

    // Placement
    this.hub.on('GameStarted', () => {
      this.gameStarted$.next();
    });

    this.hub.on('AllReady', () => {
      // Reset battle state for fresh game
      this.shotResults.set([]);
      this.eliminatedPlayerIds.set(new Set());
      this.battleWinnerId.set(null);
      this.battleWinnerUsername.set(null);
      this.battleChatMessages.set([]);
      this.allReady$.next();
    });

    // Battle events
    this.hub.on('TurnStarted', (data: { playerId: number; username: string } | number, usernameArg?: string) => {
      // Hub sends object or two separate args depending on overload
      if (typeof data === 'object') {
        this.currentTurnPlayerId.set(data.playerId);
        this.currentTurnUsername.set(data.username);
      } else {
        this.currentTurnPlayerId.set(data);
        this.currentTurnUsername.set(usernameArg ?? '');
      }
    });

    this.hub.on('ShotFired', (result: BattleShotResult) => {
      this.shotResults.update(s => [...s, result]);
    });

    this.hub.on('PlayerEliminated', (playerId: number) => {
      this.eliminatedPlayerIds.update(s => new Set([...s, playerId]));
    });

    this.hub.on('BattleOver', (data: { winnerId: number; winnerUsername: string }) => {
      this.battleWinnerId.set(data.winnerId);
      this.battleWinnerUsername.set(data.winnerUsername);
    });

    this.hub.on('BattleChatMessage', (senderId: number, senderUsername: string, text: string, timestamp: string) => {
      this.battleChatMessages.update(msgs => [
        ...msgs,
        { senderId, senderUsername, text, timestamp: new Date(timestamp) }
      ]);
    });

    return this.hub.start();
  }

  stopConnection(): void {
    this.hub?.stop();
    this.lobbyRoomId.set(null);
    this.lobbyMembers.set([]);
    this.chatMessages.set([]);
    this.battleChatMessages.set([]);
    this.onlineUserIds.set(new Set());
    this.latestInvite.set(null);
    this.isHost.set(false);
    this.myFleet.set([]);
    this.currentTurnPlayerId.set(null);
    this.shotResults.set([]);
    this.eliminatedPlayerIds.set(new Set());
    this.battleWinnerId.set(null);
    this.battleWinnerUsername.set(null);
  }

  ngOnDestroy(): void {
    this.hub?.stop();
    this.userJoined$.complete();
    this.roomCreated$.complete();
    this.gameStarted$.complete();
    this.allReady$.complete();
  }

  // в”Ђв”Ђ Presence в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

  sendGameInvite(targetUserId: number, roomId: string): void {
    this.hub?.invoke('SendGameInvite', targetUserId, roomId);
  }

  // в”Ђв”Ђ Lobby в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

  async createGame(): Promise<string> {
    const roomIdPromise = firstValueFrom(this.roomCreated$);
    await this.hub!.invoke('CreateGame');
    return roomIdPromise;
  }

  async acceptInvite(roomId: string): Promise<void> {
    this.lobbyRoomId.set(null);
    this.lobbyMembers.set([]);
    this.chatMessages.set([]);
    await this.hub!.invoke('AcceptInvite', roomId);
    this.latestInvite.set(null);
  }

  declineInvite(hostUserId: number): void {
    this.hub?.invoke('DeclineInvite', hostUserId);
    this.latestInvite.set(null);
  }

  sendLobbyChat(message: string): void {
    const roomId = this.lobbyRoomId();
    if (roomId) this.hub?.invoke('SendLobbyChat', roomId, message);
  }

  leaveLobby(): void {
    const roomId = this.lobbyRoomId();
    if (roomId && this.hub?.state === signalR.HubConnectionState.Connected) {
      this.hub.invoke('LeaveLobby', roomId);
    }
    this.lobbyRoomId.set(null);
    this.lobbyMembers.set([]);
    this.chatMessages.set([]);
    this.battleChatMessages.set([]);
    this.isHost.set(false);
    this.myFleet.set([]);
    this.currentTurnPlayerId.set(null);
    this.shotResults.set([]);
    this.eliminatedPlayerIds.set(new Set());
    this.battleWinnerId.set(null);
    this.battleWinnerUsername.set(null);
  }

  startGame(): void {
    this.hub?.invoke('StartGame');
  }

  /** Submit fleet and mark player as ready. */
  playerReady(ships: ShipPlacement[]): void {
    this.myFleet.set(ships);
    this.hub?.invoke('PlayerReady', ships);
  }

  // в”Ђв”Ђ Battle в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

  fireShot(targetId: number, row: number, col: number): void {
    this.hub?.invoke('FireShot', targetId, row, col);
  }

  sendBattleChat(message: string): void {
    const roomId = this.lobbyRoomId();
    if (roomId) this.hub?.invoke('SendBattleChat', roomId, message);
  }
}
