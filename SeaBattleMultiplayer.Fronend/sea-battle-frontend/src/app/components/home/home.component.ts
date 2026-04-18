import { Component, inject, OnInit, OnDestroy, computed, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { FriendsService } from '../../services/friends.service';
import { SignalRService } from '../../services/signalr.service';
import { Player } from '../../models/player.models';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent implements OnInit, OnDestroy {
  readonly authService = inject(AuthService);
  private readonly friendsService = inject(FriendsService);
  private readonly signalR = inject(SignalRService);
  private readonly router = inject(Router);

  private readonly allPlayers = signal<Player[]>([]);

  readonly onlinePlayers = computed(() => {
    const onlineIds = this.signalR.onlineUserIds();
    return this.allPlayers().map(p => ({ ...p, isOnline: onlineIds.has(p.id) }))
      .filter(p => p.isOnline)
      .sort((a, b) => a.username.localeCompare(b.username));
  });

  readonly offlinePlayers = computed(() => {
    const onlineIds = this.signalR.onlineUserIds();
    return this.allPlayers().map(p => ({ ...p, isOnline: onlineIds.has(p.id) }))
      .filter(p => !p.isOnline)
      .sort((a, b) => a.username.localeCompare(b.username));
  });

  private sub = new Subscription();

  async ngOnInit(): Promise<void> {
    await this.signalR.startConnection();
    this.loadPlayers();

    this.sub.add(
      this.signalR.userJoined$.subscribe(({ id, username }) => {
        const exists = this.allPlayers().some(p => p.id === id);
        if (!exists) {
          this.allPlayers.update(list => [
            ...list,
            { id, username, isOnline: true, isFriend: false }
          ]);
        }
      })
    );
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
  }

  private loadPlayers(): void {
    this.friendsService.getPlayers().subscribe(players => {
      this.allPlayers.set(players);
    });
  }

  addFriend(player: Player): void {
    this.friendsService.addFriend(player.id).subscribe(() => {
      this.allPlayers.update(list =>
        list.map(p => p.id === player.id ? { ...p, isFriend: true } : p)
      );
    });
  }

  removeFriend(player: Player): void {
    this.friendsService.removeFriend(player.id).subscribe(() => {
      this.allPlayers.update(list =>
        list.map(p => p.id === player.id ? { ...p, isFriend: false } : p)
      );
    });
  }

  async inviteToGame(player: Player): Promise<void> {
    let roomId = this.signalR.lobbyRoomId();
    if (!roomId) {
      roomId = await this.signalR.createGame();
    }
    this.signalR.sendGameInvite(player.id, roomId);
    this.router.navigate(['/lobby']);
  }
}

