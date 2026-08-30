import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { McpConfigService, McpServerConfig, McpTestResult } from './mcp-config.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.css'
})
export class SettingsComponent implements OnInit {
  private svc = inject(McpConfigService);

  configs    = signal<McpServerConfig[]>([]);
  loadError  = signal(false);
  saving     = signal(false);
  saveError  = signal<string | null>(null);
  savedMsg   = signal(false);
  testingId  = signal<string | null>(null);
  testResults = signal<Record<string, McpTestResult | undefined>>({});

  activeSection = signal('mcp-servers');

  // Plain property for the form — ngModel writes directly into this object
  editing: McpServerConfig | null = null;
  showApiKey = false;

  ngOnInit() { this.load(); }

  load(): void {
    this.loadError.set(false);
    this.svc.list().subscribe({
      next: list => this.configs.set(list),
      error: ()  => this.loadError.set(true)
    });
  }

  startAdd(): void {
    this.editing = { name: '', transportType: 'http', url: '', isEnabled: true };
    this.showApiKey = false;
  }

  startEdit(config: McpServerConfig): void {
    this.editing = { ...config };
    this.showApiKey = false;
  }

  cancelEdit(): void { this.editing = null; }

  save(): void {
    if (!this.editing || !this.editing.name?.trim()) return;
    this.saving.set(true);
    this.saveError.set(null);
    const call = this.editing.id
      ? this.svc.update(this.editing.id, this.editing)
      : this.svc.create(this.editing);

    call.subscribe({
      next: () => {
        this.saving.set(false);
        this.editing = null;
        this.savedMsg.set(true);
        setTimeout(() => this.savedMsg.set(false), 3000);
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        const status = err?.status;
        this.saveError.set(
          status === 0    ? 'Cannot reach the API — make sure the server is running.' :
          status === 400  ? 'Invalid data — check the form fields.' :
          status === 500  ? 'Server error — check the API logs.' :
                            `Unexpected error (${status ?? 'unknown'}).`
        );
      }
    });
  }

  remove(id: string): void {
    this.svc.delete(id).subscribe(() => this.load());
  }

  toggleEnabled(config: McpServerConfig): void {
    this.svc.update(config.id!, { ...config, isEnabled: !config.isEnabled })
      .subscribe(() => this.load());
  }

  test(config: McpServerConfig): void {
    this.testingId.set(config.id!);
    this.svc.test(config.id!).subscribe({
      next: result => {
        this.testResults.update(r => ({ ...r, [config.id!]: result }));
        this.testingId.set(null);
      },
      error: () => {
        this.testResults.update(r => ({ ...r, [config.id!]: { reachable: false, error: 'Request failed' } }));
        this.testingId.set(null);
      }
    });
  }

  transportLabel(type: string): string {
    return ({ http: 'HTTP', sse: 'SSE', stdio: 'Stdio' } as Record<string, string>)[type] ?? type.toUpperCase();
  }
}