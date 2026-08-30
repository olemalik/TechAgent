import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface McpServerConfig {
  id?: string;
  name: string;
  transportType: 'http' | 'sse' | 'stdio';
  url?: string;
  apiKey?: string;
  description?: string;
  isEnabled: boolean;
  createdAt?: string;
}

export interface McpTestResult {
  reachable: boolean;
  status?: number;
  error?: string;
  note?: string;
}

@Injectable({ providedIn: 'root' })
export class McpConfigService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/api/mcp-configs`;

  list(): Observable<McpServerConfig[]> {
    return this.http.get<McpServerConfig[]>(this.base);
  }

  create(c: McpServerConfig): Observable<McpServerConfig> {
    return this.http.post<McpServerConfig>(this.base, c);
  }

  update(id: string, c: McpServerConfig): Observable<McpServerConfig> {
    return this.http.put<McpServerConfig>(`${this.base}/${id}`, c);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  test(id: string): Observable<McpTestResult> {
    return this.http.post<McpTestResult>(`${this.base}/${id}/test`, {});
  }
}