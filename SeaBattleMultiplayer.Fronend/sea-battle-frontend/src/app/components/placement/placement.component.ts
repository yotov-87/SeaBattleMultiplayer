import { Component, inject, OnInit, OnDestroy, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { CommonModule } from '@angular/common';
import { SignalRService } from '../../services/signalr.service';
import { AuthService } from '../../services/auth.service';

type CellState = 'empty' | 'ship' | 'preview-ok' | 'preview-bad';

interface PlacedShip {
  id: number;
  size: number;
  row: number;
  col: number;
  horizontal: boolean;
}

const SHIP_DEFS = [
  { size: 5, label: 'Carrier',    emoji: '🚢', count: 1 },
  { size: 2, label: 'Destroyer',  emoji: '⛵', count: 3 },
  { size: 1, label: 'Patrol',     emoji: '🛥️', count: 4 },
] as const;

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

  // ── Timer ──────────────────────────────────────────────────────────────
  timeLeft = signal(25);
  isReady = false;
  private countdownId: ReturnType<typeof setInterval> | null = null;
  private sub = new Subscription();

  // ── Grid constants ─────────────────────────────────────────────────────
  readonly ROWS = Array.from({ length: 10 }, (_, i) => i);
  readonly COLS = Array.from({ length: 10 }, (_, i) => i);
  readonly ROW_LABELS = 'ABCDEFGHIJ';
  readonly SHIP_DEFS = SHIP_DEFS;

  // ── Placement state ────────────────────────────────────────────────────
  readonly placedShips = signal<PlacedShip[]>([]);
  private nextId = 1;

  selectedSize = signal<number | null>(null);
  isHorizontal = signal(true);
  hoverCell = signal<{ r: number; c: number } | null>(null);

  // ── Computed: map of r*10+c → shipId for placed ships ─────────────────
  readonly occupiedCells = computed(() => {
    const map = new Map<number, number>();
    for (const ship of this.placedShips()) {
      for (let i = 0; i < ship.size; i++) {
        const r = ship.horizontal ? ship.row     : ship.row + i;
        const c = ship.horizontal ? ship.col + i : ship.col;
        map.set(r * 10 + c, ship.id);
      }
    }
    return map;
  });

  // ── Computed: remaining count per size ─────────────────────────────────
  readonly remainingCounts = computed(() => {
    const placed = new Map<number, number>();
    for (const s of this.placedShips()) {
      placed.set(s.size, (placed.get(s.size) ?? 0) + 1);
    }
    const result = new Map<number, number>();
    for (const def of SHIP_DEFS) {
      result.set(def.size, def.count - (placed.get(def.size) ?? 0));
    }
    return result;
  });

  // ── Computed: preview cells for current hover position ─────────────────
  readonly previewCells = computed((): Map<number, boolean> => {
    const size = this.selectedSize();
    const hover = this.hoverCell();
    if (!size || !hover) return new Map();

    const horizontal = this.isHorizontal();
    const cells: { r: number; c: number }[] = [];
    for (let i = 0; i < size; i++) {
      cells.push({
        r: horizontal ? hover.r     : hover.r + i,
        c: horizontal ? hover.c + i : hover.c,
      });
    }

    const occupied = this.occupiedCells();
    const cellSet = new Set(cells.map(({ r, c }) => r * 10 + c));

    // In-bounds + no overlap
    let valid = cells.every(
      ({ r, c }) => r >= 0 && r < 10 && c >= 0 && c < 10 && !occupied.has(r * 10 + c)
    );

    // No adjacency to existing ships (ships may not touch — sea battle rules)
    if (valid) {
      outer: for (const { r, c } of cells) {
        for (let dr = -1; dr <= 1; dr++) {
          for (let dc = -1; dc <= 1; dc++) {
            if (dr === 0 && dc === 0) continue;
            const nr = r + dr, nc = c + dc;
            if (nr < 0 || nr >= 10 || nc < 0 || nc >= 10) continue;
            const nkey = nr * 10 + nc;
            if (occupied.has(nkey) && !cellSet.has(nkey)) {
              valid = false;
              break outer;
            }
          }
        }
      }
    }

    const result = new Map<number, boolean>();
    for (const { r, c } of cells) result.set(r * 10 + c, valid);
    return result;
  });

  // ── Computed: full 10×10 cell state matrix ─────────────────────────────
  readonly cellStates = computed((): CellState[][] => {
    const occupied = this.occupiedCells();
    const preview  = this.previewCells();
    return this.ROWS.map(r =>
      this.COLS.map(c => {
        const key = r * 10 + c;
        if (preview.has(key)) return preview.get(key) ? 'preview-ok' : 'preview-bad';
        if (occupied.has(key)) return 'ship';
        return 'empty';
      })
    );
  });

  // ── Computed: all required ships have been placed ──────────────────────
  readonly allPlaced = computed(() =>
    [...this.remainingCounts().values()].every(v => v === 0)
  );

  // ── Lifecycle ──────────────────────────────────────────────────────────
  ngOnInit(): void {
    this.sub.add(
      this.signalR.allReady$.subscribe(() => {
        this.clearCountdown();
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

  // ── User actions ───────────────────────────────────────────────────────

  selectShip(size: number): void {
    if ((this.remainingCounts().get(size) ?? 0) === 0) return;
    this.selectedSize.update(s => (s === size ? null : size));
  }

  rotate(): void {
    this.isHorizontal.update(h => !h);
  }

  onCellHover(r: number, c: number): void {
    this.hoverCell.set({ r, c });
  }

  onGridLeave(): void {
    this.hoverCell.set(null);
  }

  onCellClick(r: number, c: number): void {
    const size = this.selectedSize();
    if (!size) {
      // Remove ship at this cell (if any)
      const shipId = this.occupiedCells().get(r * 10 + c);
      if (shipId !== undefined) {
        this.placedShips.update(ships => ships.filter(s => s.id !== shipId));
      }
      return;
    }

    if (!this.previewCells().get(r * 10 + c)) return; // invalid position

    this.placedShips.update(ships => [
      ...ships,
      { id: this.nextId++, size, row: r, col: c, horizontal: this.isHorizontal() },
    ]);

    // Auto-deselect when this type is fully placed
    if ((this.remainingCounts().get(size) ?? 0) === 0) {
      this.selectedSize.set(null);
    }
  }

  onGridRightClick(e: MouseEvent): void {
    e.preventDefault();
    this.rotate();
  }

  clearAll(): void {
    this.placedShips.set([]);
    this.selectedSize.set(null);
  }

  markReady(): void {
    if (this.isReady) return;
    this.isReady = true;
    this.clearCountdown();
    this.signalR.playerReady();
  }

  shipCells(size: number): number[] {
    return Array.from({ length: size });
  }

  private clearCountdown(): void {
    if (this.countdownId !== null) {
      clearInterval(this.countdownId);
      this.countdownId = null;
    }
  }
}
