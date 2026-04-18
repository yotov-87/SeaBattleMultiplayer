import { Injectable, inject, signal, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import { GameInvite, LobbyMember, ChatMessage } from '../models/player.models';

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private readonly authService = inject(AuthService);

  private hub: signalR.HubConnection | null = null;

  // ── Presence ──────────────────────────────────────────────────────────
  readonly onlineUserIds = signal<Set<number>>(new Set());
  readonly latestInvite = signal<GameInvite | null>(null);
  readonly userJoined$ = new Subject<{ id: number; username: string }>();

  // ── Lobby ──────────────────────────────────────────────────────────────
  readonly lobbyRoomId = signal<string | null>(null);
  readonly lobbyMembers = signal<LobbyMember[]>([]);
  readonly chatMessages = signal<ChatMessage[]>([]);
  readonly isHost = signal<boolean>(false);

  private readonly roomCreated$ = new Subject<string>();

  // ── Game phase ────────────────────────────────────────────────────
  readonly gameStarted$ = new Subject<void>();
  readonly allReady$ = new Subject<void>();
  readonly battleChatMessages = signal<ChatMessage[]>([]);

  // ── Connection ─────────────────────────────────────────────────────────

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

    // Game phase
    this.hub.on('GameStarted', () => {
      this.gameStarted$.next();
    });

    this.hub.on('AllReady', () => {
      this.allReady$.next();
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
  }

  ngOnDestroy(): void {
    this.hub?.stop();
    this.userJoined$.complete();
    this.roomCreated$.complete();
    this.gameStarted$.complete();
    this.allReady$.complete();
  }

  // ── Presence methods ───────────────────────────────────────────────────

  sendGameInvite(targetUserId: number, roomId: string): void {
    this.hub?.invoke('SendGameInvite', targetUserId, roomId);
  }

  // ── Lobby methods ──────────────────────────────────────────────────────

  async createGame(): Promise<string> {
    const roomIdPromise = firstValueFrom(this.roomCreated$);
    await this.hub!.invoke('CreateGame');
    return roomIdPromise;
  }

  async acceptInvite(roomId: string): Promise<void> {
    // Clear local lobby state before joining the new room
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
  }

  startGame(): void {
    this.hub?.invoke('StartGame');
  }

  playerReady(): void {
    this.hub?.invoke('PlayerReady');
  }

  sendBattleChat(message: string): void {
    const roomId = this.lobbyRoomId();
    if (roomId) this.hub?.invoke('SendBattleChat', roomId, message);
  }
}

