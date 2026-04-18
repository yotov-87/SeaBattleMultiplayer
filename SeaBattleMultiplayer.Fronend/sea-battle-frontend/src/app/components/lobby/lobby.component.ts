import { Component, inject, OnInit, OnDestroy, computed, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../services/auth.service';
import { FriendsService } from '../../services/friends.service';
import { SignalRService } from '../../services/signalr.service';
import { Player } from '../../models/player.models';

@Component({
  selector: 'app-lobby',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './lobby.component.html',
  styleUrl: './lobby.component.scss'
})
export class LobbyComponent implements OnInit, OnDestroy {
  readonly authService = inject(AuthService);
  readonly signalR = inject(SignalRService);
  private readonly friendsService = inject(FriendsService);
  private readonly router = inject(Router);

  private readonly allPlayers = signal<Player[]>([]);
  chatInput = '';
  isCreating = false;

  private sub = new Subscription();

  /** Online players not yet in the lobby, friends first */
  readonly invitablePlayers = computed(() => {
    const onlineIds = this.signalR.onlineUserIds();
    const lobbyIds = new Set(this.signalR.lobbyMembers().map(m => m.id));

    return this.allPlayers()
      .map(p => ({ ...p, isOnline: onlineIds.has(p.id) }))
      .filter(p => p.isOnline && !lobbyIds.has(p.id))
      .sort((a, b) => {
        if (a.isFriend !== b.isFriend) return a.isFriend ? -1 : 1;
        return a.username.localeCompare(b.username);
      });
  });

  async ngOnInit(): Promise<void> {
    await this.signalR.startConnection();

    this.friendsService.getPlayers().subscribe(players => {
      this.allPlayers.set(players);
    });

    this.sub.add(
      this.signalR.userJoined$.subscribe(({ id, username }) => {
        if (!this.allPlayers().some(p => p.id === id)) {
          this.allPlayers.update(list => [
            ...list,
            { id, username, isOnline: true, isFriend: false }
          ]);
        }
      })
    );

    // Create room only if we don't already have one (e.g. navigated from home invite)
    if (!this.signalR.lobbyRoomId()) {
      this.isCreating = true;
      await this.signalR.createGame();
      this.isCreating = false;
    }
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
    this.signalR.leaveLobby();
  }

  invite(player: Player): void {
    const roomId = this.signalR.lobbyRoomId();
    if (roomId) {
      this.signalR.sendGameInvite(player.id, roomId);
    }
  }

  sendChat(): void {
    const msg = this.chatInput.trim();
    if (!msg) return;
    this.signalR.sendLobbyChat(msg);
    this.chatInput = '';
  }

  leaveLobby(): void {
    this.signalR.leaveLobby();
    this.router.navigate(['/home']);
  }
}
