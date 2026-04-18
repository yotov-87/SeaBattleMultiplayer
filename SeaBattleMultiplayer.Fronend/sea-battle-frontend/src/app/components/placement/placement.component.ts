import { Component, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { CommonModule } from '@angular/common';
import { SignalRService } from '../../services/signalr.service';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-placement',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './placement.component.html',
  styleUrl: './placement.component.scss'
})
export class PlacementComponent implements OnInit, OnDestroy {
  readonly signalR = inject(SignalRService);
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  timeLeft = signal(25);
  isReady = false;

  /** 10×10 grid — each cell is false (empty) for now */
  readonly rows = Array.from({ length: 10 }, (_, r) => r);
  readonly cols = Array.from({ length: 10 }, (_, c) => c);

  private countdownId: ReturnType<typeof setInterval> | null = null;
  private sub = new Subscription();
  private navigatingToBattle = false;

  ngOnInit(): void {
    this.sub.add(
      this.signalR.allReady$.subscribe(() => {
        this.clearCountdown();
        this.navigatingToBattle = true;
        this.router.navigate(['/battle']);
      })
    );

    this.countdownId = setInterval(() => {
      this.timeLeft.update(t => t - 1);
      if (this.timeLeft() <= 0) {
        this.clearCountdown();
        this.markReady();
      }
    }, 1000);
  }

  ngOnDestroy(): void {
    this.clearCountdown();
    this.sub.unsubscribe();
  }

  markReady(): void {
    if (this.isReady) return;
    this.isReady = true;
    this.clearCountdown();
    this.signalR.playerReady();
  }

  private clearCountdown(): void {
    if (this.countdownId !== null) {
      clearInterval(this.countdownId);
      this.countdownId = null;
    }
  }
}
