import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { SignalRService } from '../../services/signalr.service';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss'
})
export class HeaderComponent {
  readonly authService = inject(AuthService);
  private readonly signalR = inject(SignalRService);
  private readonly router = inject(Router);

  logout(): void {
    this.signalR.stopConnection();
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
