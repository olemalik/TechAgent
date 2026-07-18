import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ChatHistoryEntry, SessionSummary } from './models/chat.model';
import { SESSION_KEY } from './constants/chat.constants';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly apiUrl = `${environment.apiUrl}/api/chat`;
  private readonly http = inject(HttpClient);
  private sessionId: string | null = localStorage.getItem(SESSION_KEY);

  sendStreaming(
    message: string,
    signal: AbortSignal,
    attachment?: { name: string; url: string; contentType: string } | null
  ): Promise<ReadableStreamDefaultReader<Uint8Array>> {
    return fetch(`${this.apiUrl}/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        message,
        sessionId: this.sessionId,
        attachmentName: attachment?.name ?? null,
        attachmentUrl: attachment?.url ?? null,
        attachmentContentType: attachment?.contentType ?? null,
      }),
      signal
    }).then(res => {
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      return res.body!.getReader();
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

  submitFeedback(messageId: number, score: 1 | -1, correction?: string): Observable<{ promoted: boolean }> {
    return this.http.post<{ promoted: boolean }>(`${this.apiUrl}/feedback`, {
      messageId,
      score,
      correction: correction ?? null
    });
  }

  uploadFile(file: File): Observable<{ fileName: string; url: string; contentType: string; sizeBytes: number }> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<{ fileName: string; url: string; contentType: string; sizeBytes: number }>(
      `${environment.apiUrl}/api/file/upload`,
      form
    );
  }
}
