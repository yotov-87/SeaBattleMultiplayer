import { Component, inject, OnInit, OnDestroy, computed, signal,
         ElementRef, ViewChild, AfterViewChecked } from "@angular/core";
import { CommonModule } from "@angular/common";
import { FormsModule } from "@angular/forms";
import { Router } from "@angular/router";
import { Subscription } from "rxjs";
import { AuthService } from "../../services/auth.service";
import { SignalRService } from "../../services/signalr.service";
import { ShipPlacement, FleetMoveDirection } from "../../models/player.models";

// Sea cell visual kinds
type CellKind =
  | "fog"                          // outside fog radius - unknown
  | "sea"                          // visible, empty
  | "my-ship"                      // my ship cell
  | "my-area"                      // my fleet area, no ship
  | "enemy-area"                   // in-range enemy fleet cell (empty/unknown)
  | "shot-miss" | "shot-hit" | "shot-sunk"        // shots I fired
  | "recv-miss" | "recv-hit" | "recv-sunk";        // shots fired at me

@Component({
  selector: "app-battle",
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: "./battle.component.html",
  styleUrl: "./battle.component.scss"
})
export class BattleComponent implements OnInit, OnDestroy, AfterViewChecked {
  readonly authService = inject(AuthService);
  readonly signalR     = inject(SignalRService);
  private readonly router = inject(Router);

  @ViewChild("seaContainer") seaContainerRef?: ElementRef<HTMLElement>;

  // ── Constants ─────────────────────────────────────────────────────────────
  readonly SEA        = 50;
  readonly CELL_PX    = 11;    // px per cell
  readonly FOG_RADIUS = 12;    // Euclidean fog radius in sea cells
  readonly RANGE      = 2;     // Chebyshev gap to allow shooting

  readonly SEA_ROWS   = Array.from({ length: 50 }, (_, i) => i);
  readonly SEA_COLS   = Array.from({ length: 50 }, (_, i) => i);

  // ── UI state ──────────────────────────────────────────────────────────────
  chatInput    = "";
  autoShot     = false;
  private needsScroll = true;

  // ── Turn timer ────────────────────────────────────────────────────────────
  turnTimeLeft = signal(15);
  private countdownId: ReturnType<typeof setInterval> | null = null;
  private sub = new Subscription();

  // ── Core computed ─────────────────────────────────────────────────────────
  readonly myId = computed(() => this.authService.userId() ?? -1);

  readonly myOffset = computed(() => {
    return this.signalR.fleetPositions().get(this.myId()) ?? { offsetRow: 0, offsetCol: 0 };
  });

  readonly opponents = computed(() => {
    const myId = this.myId();
    const elim = this.signalR.eliminatedPlayerIds();
    return this.signalR.lobbyMembers().filter(m => m.id !== myId && !elim.has(m.id));
  });

  readonly inRangeEnemies = computed(() => {
    const myPos     = this.myOffset();
    const positions = this.signalR.fleetPositions();
    return this.opponents().filter(opp => {
      const ep = positions.get(opp.id);
      if (!ep) return false;
      const rGap = Math.max(0, Math.max(
        myPos.offsetRow - (ep.offsetRow + 9),
        ep.offsetRow    - (myPos.offsetRow + 9)
      ));
      const cGap = Math.max(0, Math.max(
        myPos.offsetCol - (ep.offsetCol + 9),
        ep.offsetCol    - (myPos.offsetCol + 9)
      ));
      return Math.max(rGap, cGap) <= this.RANGE;
    });
  });

  readonly isMyTurn = computed(() =>
    this.signalR.currentTurnPlayerId() === this.myId()
  );

  readonly canShoot = computed(() =>
    this.isMyTurn() && this.inRangeEnemies().length > 0
  );

  // ── Sea grid (2D array of CellKind) ───────────────────────────────────────
  readonly seaGrid = computed((): CellKind[][] => {
    const SEA      = this.SEA;
    const myId     = this.myId();
    const myPos    = this.myOffset();
    const shots    = this.signalR.shotResults();
    const ships    = this.signalR.myFleet();
    const positions = this.signalR.fleetPositions();
    const inRange  = this.inRangeEnemies();
    const fogR     = this.FOG_RADIUS;

    // My ship cells (sea-absolute key = row*SEA+col)
    const myShipSet = new Set<number>();
    for (const ship of ships) {
      for (let i = 0; i < ship.size; i++) {
        const r = (ship.horizontal ? ship.row     : ship.row + i) + myPos.offsetRow;
        const c = (ship.horizontal ? ship.col + i : ship.col)     + myPos.offsetCol;
        myShipSet.add(r * SEA + c);
      }
    }

    // Shot results: map sea-key -> CellKind
    const shotMap = new Map<number, CellKind>();
    for (const shot of shots) {
      const baseKey = shot.row * SEA + shot.col;
      if (shot.shooterId === myId) {
        const k: CellKind = shot.result === "sunk" ? "shot-sunk"
                         : shot.result === "hit"  ? "shot-hit" : "shot-miss";
        shotMap.set(baseKey, k);
        if (shot.sunkCells) {
          for (const sc of shot.sunkCells) shotMap.set(sc.row * SEA + sc.col, "shot-sunk");
        }
      } else if (shot.targetId === myId) {
        const k: CellKind = shot.result === "sunk" ? "recv-sunk"
                         : shot.result === "hit"  ? "recv-hit" : "recv-miss";
        shotMap.set(baseKey, k);
        if (shot.sunkCells) {
          for (const sc of shot.sunkCells) shotMap.set(sc.row * SEA + sc.col, "recv-sunk");
        }
      }
    }

    // Enemy fleet areas visible when in range
    const enemyAreaSet = new Set<number>();
    for (const opp of inRange) {
      const ep = positions.get(opp.id);
      if (!ep) continue;
      for (let r = ep.offsetRow; r < ep.offsetRow + 10; r++)
        for (let c = ep.offsetCol; c < ep.offsetCol + 10; c++)
          enemyAreaSet.add(r * SEA + c);
    }

    const centerR = myPos.offsetRow + 4.5;
    const centerC = myPos.offsetCol + 4.5;

    return Array.from({ length: SEA }, (_, r) =>
      Array.from({ length: SEA }, (_, c): CellKind => {
        const key  = r * SEA + c;

        // Shot results are always shown (even outside fog)
        const shotKind = shotMap.get(key);
        if (shotKind) return shotKind;

        // Fog check
        const dist = Math.sqrt((r - centerR) ** 2 + (c - centerC) ** 2);
        if (dist > fogR) return "fog";

        if (myShipSet.has(key)) return "my-ship";
        if (r >= myPos.offsetRow && r < myPos.offsetRow + 10 &&
            c >= myPos.offsetCol && c < myPos.offsetCol + 10)  return "my-area";
        if (enemyAreaSet.has(key)) return "enemy-area";
        return "sea";
      })
    );
  });

  // ── Helpers ───────────────────────────────────────────────────────────────

  /** Returns the targetId if (r,c) is inside an in-range enemy fleet area. */
  getCellTarget(r: number, c: number): number | null {
    const positions = this.signalR.fleetPositions();
    for (const opp of this.inRangeEnemies()) {
      const ep = positions.get(opp.id);
      if (!ep) continue;
      if (r >= ep.offsetRow && r < ep.offsetRow + 10 &&
          c >= ep.offsetCol && c < ep.offsetCol + 10)
        return opp.id;
    }
    return null;
  }

  // ── Actions ───────────────────────────────────────────────────────────────

  onCellClick(r: number, c: number): void {
    if (!this.isMyTurn()) return;
    const targetId = this.getCellTarget(r, c);
    if (targetId === null) return;
    if (this.seaGrid()[r][c] !== "enemy-area") return; // already shot
    this.signalR.fireShot(targetId, r, c);
  }

  moveFleet(dir: FleetMoveDirection): void {
    if (!this.isMyTurn()) return;
    this.signalR.moveFleet(dir);
  }

  sendChat(): void {
    const msg = this.chatInput.trim();
    if (!msg) return;
    this.signalR.sendBattleChat(msg);
    this.chatInput = "";
  }

  leaveGame(): void {
    this.signalR.leaveLobby();
    this.router.navigate(["/home"]);
  }

  opponentName(id: number): string {
    return this.signalR.lobbyMembers().find(m => m.id === id)?.username ?? `Player ${id}`;
  }

  // ── Auto-shot ─────────────────────────────────────────────────────────────

  private fireAutoShot(): void {
    const inRange = this.inRangeEnemies();
    const grid    = this.seaGrid();
    const positions = this.signalR.fleetPositions();

    if (inRange.length > 0) {
      const target = inRange[Math.floor(Math.random() * inRange.length)];
      const ep = positions.get(target.id);
      if (!ep) return;
      const candidates: [number, number][] = [];
      for (let r = ep.offsetRow; r < ep.offsetRow + 10; r++)
        for (let c = ep.offsetCol; c < ep.offsetCol + 10; c++)
          if (grid[r]?.[c] === "enemy-area") candidates.push([r, c]);
      if (candidates.length > 0) {
        const [r, c] = candidates[Math.floor(Math.random() * candidates.length)];
        this.signalR.fireShot(target.id, r, c);
      }
    } else {
      // No in-range enemy — move toward nearest
      const dirs: FleetMoveDirection[] = ["up", "down", "left", "right"];
      this.signalR.moveFleet(dirs[Math.floor(Math.random() * dirs.length)]);
    }
  }

  // ── Lifecycle ─────────────────────────────────────────────────────────────

  ngOnInit(): void {
    if (!this.signalR.lobbyRoomId()) {
      this.router.navigate(["/home"]);
      return;
    }

    let lastTurnId: number | null = null;
    const id = setInterval(() => {
      const current = this.signalR.currentTurnPlayerId();
      if (current !== lastTurnId) {
        lastTurnId = current;
        this.restartCountdown();
        this.needsScroll = true;
        if (this.autoShot && current === this.myId()) this.fireAutoShot();
      }
    }, 100);
    this.sub.add(new Subscription(() => clearInterval(id)));
  }

  ngAfterViewChecked(): void {
    if (this.needsScroll) {
      this.scrollToFleet();
      this.needsScroll = false;
    }
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
    this.clearCountdown();
  }

  // ── Auto-scroll to keep my fleet visible ──────────────────────────────────

  scrollToFleet(): void {
    const el = this.seaContainerRef?.nativeElement;
    if (!el) return;
    const { offsetRow, offsetCol } = this.myOffset();
    const centerPx = (offsetRow + 4.5) * this.CELL_PX;
    const centerCpx = (offsetCol + 4.5) * this.CELL_PX;
    el.scrollTop  = centerPx  - el.clientHeight / 2;
    el.scrollLeft = centerCpx - el.clientWidth  / 2;
  }

  // ── Turn countdown ────────────────────────────────────────────────────────

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
}
