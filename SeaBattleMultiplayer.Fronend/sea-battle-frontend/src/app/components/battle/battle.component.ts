import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { SignalRService } from '../../services/signalr.service';

@Component({
  selector: 'app-battle',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './battle.component.html',
  styleUrl: './battle.component.scss'
})
export class BattleComponent implements OnInit {
  readonly authService = inject(AuthService);
  readonly signalR = inject(SignalRService);
  private readonly router = inject(Router);

  chatInput = '';

  ngOnInit(): void {
    // Hub connection is already alive from lobby/placement phase.
    // If the user lands here directly (e.g. page refresh) there is no room,
    // redirect them home.
    if (!this.signalR.lobbyRoomId()) {
      this.router.navigate(['/home']);
    }
  }

  sendChat(): void {
    const msg = this.chatInput.trim();
    if (!msg) return;
    this.signalR.sendBattleChat(msg);
    this.chatInput = '';
  }

  leaveGame(): void {
    this.signalR.leaveLobby();
    this.router.navigate(['/home']);
  }
}
