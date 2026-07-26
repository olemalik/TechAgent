import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface DocumentConflict {
  documentId: string;
  fileName: string;
  /** Cosine similarity 0–1 between full-document centroids. */
  similarity: number;
}

export interface DocumentStatus {
  id: string;
  fileName: string;
  status: 'Pending' | 'Processing' | 'Indexed' | 'Failed' | 'PendingReview' | 'Superseded';
  chunkCount: number;
  indexedAt?: string;
  errorMessage?: string;
  /** Non-null = document belongs to a recurring series; future similar uploads auto-index. */
  seriesId?: string;
  /** Populated only when status === 'PendingReview' */
  conflicts?: DocumentConflict[];
}

export interface UploadResponse {
  id: string;
  fileName: string;
  status: string;
  message: string;
}

export type ResolveAction = 'replace' | 'keep-both' | 'add-to-series' | 'cancel';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/api/documents`;

  list(): Observable<DocumentStatus[]> {
    return this.http.get<DocumentStatus[]>(this.apiUrl);
  }

  getStatus(id: string): Observable<DocumentStatus> {
    return this.http.get<DocumentStatus>(`${this.apiUrl}/${id}/status`);
  }

  upload(file: File): Observable<UploadResponse> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<UploadResponse>(`${this.apiUrl}/upload`, form);
  }

  resolve(id: string, action: ResolveAction, replaceIds?: string[]): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/${id}/resolve`, { action, replaceIds });
  }
}