import { Component, inject, OnInit, OnDestroy, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { SignalRService } from '../../services/signalr.service';
import { ShipPlacement } from '../../models/player.models';

type MyCellState     = 'empty' | 'ship' | 'hit-on-me' | 'miss-on-me' | 'sunk-on-me';
type EnemyCellState  = 'unknown' | 'miss' | 'hit' | 'sunk';

@Component({
  selector: 'app-battle',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './battle.component.html',
  styleUrl: './battle.component.scss'
})
export class BattleComponent implements OnInit, OnDestroy {
  readonly authService = inject(AuthService);
  readonly signalR     = inject(SignalRService);
  private readonly router = inject(Router);

  readonly ROWS       = Array.from({ length: 10 }, (_, i) => i);
  readonly COLS       = Array.from({ length: 10 }, (_, i) => i);
  readonly ROW_LABELS = 'ABCDEFGHIJ';

  chatInput     = '';
  autoShot      = false;
  selectedTarget  = signal<number | null>(null);
  myBoardCellSize = signal(16); // px — default small, user can zoom

  readonly MIN_CELL = 10;
  readonly MAX_CELL = 36;

  zoomMyBoard(delta: number): void {
    this.myBoardCellSize.update(v => Math.min(this.MAX_CELL, Math.max(this.MIN_CELL, v + delta)));
  }

  // 15-second turn timer (UI only — enforcement is on backend)
  turnTimeLeft = signal(15);
  private countdownId: ReturnType<typeof setInterval> | null = null;
  private sub = new Subscription();

  // ── Computed helpers ───────────────────────────────────────────────────

  readonly myId = computed(() => this.authService.userId() ?? -1);

  /** Other players still alive (lobby members minus me minus eliminated) */
  readonly opponents = computed(() => {
    const myId       = this.myId();
    const eliminated = this.signalR.eliminatedPlayerIds();
    return this.signalR.lobbyMembers().filter(m => m.id !== myId && !eliminated.has(m.id));
  });

  readonly isMyTurn = computed(() =>
    this.signalR.currentTurnPlayerId() === this.myId()
  );

  // ── My board: ships + shots received ──────────────────────────────────

  private readonly myOccupied = computed(() => {
    const map = new Map<number, true>();
    for (const ship of this.signalR.myFleet()) {
      for (let i = 0; i < ship.size; i++) {
        const r = ship.horizontal ? ship.row     : ship.row + i;
        const c = ship.horizontal ? ship.col + i : ship.col;
        map.set(r * 10 + c, true);
      }
    }
    return map;
  });

  /** Cells of fully-sunk ships on my board */
  private readonly mySunkCells = computed(() => {
    const sunk = new Set<number>();
    const shotsOnMe = this.signalR.shotResults().filter(s => s.targetId === this.myId());
    for (const shot of shotsOnMe) {
      if (shot.sunkCells) {
        for (const c of shot.sunkCells) sunk.add(c.row * 10 + c.col);
      }
    }
    return sunk;
  });

  readonly myBoard = computed((): MyCellState[][] => {
    const occupied  = this.myOccupied();
    const sunkCells = this.mySunkCells();
    const shotsOnMe = this.signalR.shotResults().filter(s => s.targetId === this.myId());

    return this.ROWS.map(r =>
      this.COLS.map(c => {
        const key  = r * 10 + c;
        const shot = shotsOnMe.find(s => s.row === r && s.col === c);
        if (shot) {
          if (sunkCells.has(key)) return 'sunk-on-me';
          return shot.result === 'miss' ? 'miss-on-me' : 'hit-on-me';
        }
        if (occupied.has(key)) return 'ship';
        return 'empty';
      })
    );
  });

  // ── Enemy board: shots I fired at selected target ──────────────────────

  readonly enemyBoard = computed((): EnemyCellState[][] => {
    const targetId = this.selectedTarget();
    if (targetId === null) return this.ROWS.map(() => this.COLS.map(() => 'unknown'));

    // Only shots I personally fired at this target
    const myShots = this.signalR.shotResults()
      .filter(s => s.shooterId === this.myId() && s.targetId === targetId);

    // All ship cells that I personally caused to sink:
    // a cell counts as 'sunk' if it appears in sunkCells of one of MY sinking shots
    // AND I have a direct shot at that cell (so I don't reveal cells other players hit)
    const myShotKeys = new Set<number>(myShots.map(s => s.row * 10 + s.col));
    const sunkKeys = new Set<number>();
    for (const shot of myShots) {
      if (shot.sunkCells) {
        for (const c of shot.sunkCells) {
          const key = c.row * 10 + c.col;
          if (myShotKeys.has(key)) sunkKeys.add(key);
        }
      }
    }

    return this.ROWS.map(r =>
      this.COLS.map(c => {
        const key = r * 10 + c;
        if (sunkKeys.has(key)) return 'sunk';
        const shot = myShots.find(s => s.row === r && s.col === c);
        if (!shot) return 'unknown';
        return shot.result === 'miss' ? 'miss' : 'hit';
      })
    );
  });

  // ── Lifecycle ──────────────────────────────────────────────────────────

  ngOnInit(): void {
    if (!this.signalR.lobbyRoomId()) {
      this.router.navigate(['/home']);
      return;
    }

    // Auto-select first opponent
    const opps = this.opponents();
    if (opps.length > 0) this.selectedTarget.set(opps[0].id);

    // Subscribe to turn changes
    this.sub.add(
      // We watch the signal via effect-like subscription using interval-free polling
      // Instead, react to TurnStarted via currentTurnPlayerId signal changes
      // Angular reactive: component re-evaluates computed on signal change
      // We use a raw subscription to trigger the timer restart when it's my turn
      // Use a simple approach: watch the hub directly via a subject — it's already set.
      // The cleanest way is to listen for `TurnStarted` effect in ngOnInit.
      // We'll do it by subscribing to a synthetic observable from the signal.
      this.watchTurnChanges()
    );
  }

  private watchTurnChanges(): Subscription {
    // Re-start the countdown whenever currentTurnPlayerId changes.
    // We use a micro-polling approach via setInterval, but signal-based.
    let lastTurnId: number | null = null;
    const id = setInterval(() => {
      const current = this.signalR.currentTurnPlayerId();
      if (current !== lastTurnId) {
        lastTurnId = current;
        this.restartCountdown();

        // Auto-shot: fire immediately when it becomes my turn
        if (this.autoShot && current === this.myId()) {
          this.fireAutoShot();
        }
      }
    }, 100);
    return new Subscription(() => clearInterval(id));
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
    this.clearCountdown();
  }

  // ── Actions ────────────────────────────────────────────────────────────

  selectTarget(id: number): void {
    this.selectedTarget.set(id);
  }

  onEnemyCellClick(r: number, c: number): void {
    if (!this.isMyTurn()) return;
    const targetId = this.selectedTarget();
    if (targetId === null) return;
    // Don't re-fire already-shot cells
    if (this.enemyBoard()[r][c] !== 'unknown') return;
    this.signalR.fireShot(targetId, r, c);
  }

  private fireAutoShot(): void {
    const targetId = this.selectedTarget() ?? this.opponents()[0]?.id;
    if (targetId === undefined) return;
    const board = this.signalR.shotResults()
      .filter(s => s.shooterId === this.myId() && s.targetId === targetId)
      .map(s => s.row * 10 + s.col);
    const fired = new Set(board);
    const cells = [];
    for (let r = 0; r < 10; r++)
      for (let c = 0; c < 10; c++)
        if (!fired.has(r * 10 + c)) cells.push({ r, c });
    if (cells.length === 0) return;
    const pick = cells[Math.floor(Math.random() * cells.length)];
    this.signalR.fireShot(targetId, pick.r, pick.c);
  }

  private restartCountdown(): void {
    this.clearCountdown();
    this.turnTimeLeft.set(15);
    this.countdownId = setInterval(() => {
      this.turnTimeLeft.update(t => Math.max(0, t - 1));
    }, 1000);
  }

  private clearCountdown(): void {
    if (this.countdownId !== null) {
      clearInterval(this.countdownId);
      this.countdownId = null;
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

  cellLabel(r: number, c: number): string {
    return `${this.ROW_LABELS[r]}${c + 1}`;
  }

  opponentName(id: number): string {
    return this.signalR.lobbyMembers().find(m => m.id === id)?.username ?? `Player ${id}`;
  }
}

