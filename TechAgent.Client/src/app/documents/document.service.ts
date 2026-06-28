import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface DocumentStatus {
  id: string;
  fileName: string;
  status: 'Processing' | 'Indexed' | 'Failed';
  chunkCount: number;
  indexedAt?: string;
  errorMessage?: string;
}

export interface UploadResponse {
  id: string;
  fileName: string;
  status: string;
  message: string;
}

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
}