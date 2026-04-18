import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Player } from '../models/player.models';

@Injectable({ providedIn: 'root' })
export class FriendsService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/friends`;

  getPlayers(): Observable<Player[]> {
    return this.http.get<Player[]>(`${this.apiUrl}/players`);
  }

  addFriend(targetUserId: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${targetUserId}/add`, {});
  }

  removeFriend(targetUserId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${targetUserId}/remove`);
  }
}
