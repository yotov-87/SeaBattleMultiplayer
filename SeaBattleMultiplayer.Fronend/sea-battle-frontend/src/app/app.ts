import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { HeaderComponent } from './components/header/header.component';
import { SignalRService } from './services/signalr.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HeaderComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  readonly signalR = inject(SignalRService);
  private readonly router = inject(Router);

  async acceptInvite(): Promise<void> {
    const invite = this.signalR.latestInvite();
    if (!invite) return;
    await this.signalR.acceptInvite(invite.roomId);
    this.router.navigate(['/lobby']);
  }

  declineInvite(): void {
    const invite = this.signalR.latestInvite();
    if (!invite) return;
    this.signalR.declineInvite(invite.senderId);
  }
}
