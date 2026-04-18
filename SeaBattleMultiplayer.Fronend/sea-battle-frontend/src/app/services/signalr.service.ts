import { Injectable, inject, signal, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import { GameInvite } from '../models/player.models';

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private readonly authService = inject(AuthService);

  private hub: signalR.HubConnection | null = null;

  readonly onlineUserIds = signal<Set<number>>(new Set());
  readonly latestInvite = signal<GameInvite | null>(null);

  /** Emits whenever a user comes online that the client should know about */
  readonly userJoined$ = new Subject<{ id: number; username: string }>();

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

    this.hub.on('GameInviteReceived', (senderId: number, senderUsername: string) => {
      this.latestInvite.set({ senderId, senderUsername });
    });

    return this.hub.start();
  }

  sendGameInvite(targetUserId: number): Promise<void> {
    return this.hub?.invoke('SendGameInvite', targetUserId) ?? Promise.resolve();
  }

  stopConnection(): void {
    this.hub?.stop();
    this.userJoined$.complete();
  }

  ngOnDestroy(): void {
    this.stopConnection();
  }
}
