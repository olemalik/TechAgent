import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ChatApiResponse, ChatHistoryEntry, SessionSummary } from './models/chat.model';
import { SESSION_KEY } from './constants/chat.constants';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly apiUrl = `${environment.apiUrl}/api/chat`;
  private readonly http = inject(HttpClient);
  private sessionId: string | null = localStorage.getItem(SESSION_KEY);

  send(message: string): Observable<ChatApiResponse> {
    return this.http.post<ChatApiResponse>(this.apiUrl, {
      message,
      sessionId: this.sessionId
    });
  }

  setSessionId(id: string): void {
    this.sessionId = id;
    localStorage.setItem(SESSION_KEY, id);
  }

  getSessionId(): string | null {
    return this.sessionId;
  }

  getHistory(): Observable<ChatHistoryEntry[]> {
    return this.http.get<ChatHistoryEntry[]>(`${this.apiUrl}/history/${this.sessionId}`);
  }

  getSessions(): Observable<SessionSummary[]> {
    return this.http.get<SessionSummary[]>(`${this.apiUrl}/sessions`);
  }

  deleteSession(sessionId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/history/${sessionId}`);
  }

  clearSession(): void {
    this.sessionId = null;
    localStorage.removeItem(SESSION_KEY);
  }
}
