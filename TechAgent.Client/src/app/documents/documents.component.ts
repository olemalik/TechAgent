import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DocumentService, DocumentStatus, ResolveAction } from './document.service';
import { FileUploaderComponent } from '../shared/file-uploader/file-uploader.component';
import { interval, Subscription } from 'rxjs';
import { switchMap } from 'rxjs/operators';

@Component({
  selector: 'app-documents',
  standalone: true,
  imports: [CommonModule, FileUploaderComponent],
  templateUrl: './documents.component.html',
  styleUrl: './documents.component.css'
})
export class DocumentsComponent implements OnInit, OnDestroy {
  private service = inject(DocumentService);

  documents   = signal<DocumentStatus[]>([]);
  loadError   = signal(false);
  uploadError = signal<string | null>(null);
  uploading   = signal(false);
  isDragging  = signal(false);
  resolving   = signal(false);

  // Documents waiting for user decision — shown above the list as conflict cards.
  pendingReviews = computed(() =>
    this.documents().filter(d => d.status === 'PendingReview')
  );

  private pollSub?: Subscription;
  private dragCount = 0;

  ngOnInit(): void {
    this.loadDocuments();
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  loadDocuments(): void {
    this.loadError.set(false);
    this.service.list().subscribe({
      next: docs => {
        this.documents.set(docs);
        if (docs.some(d => d.status === 'Processing')) this.startPolling();
      },
      error: () => this.loadError.set(true)
    });
  }

  // ── Drag and drop ────────────────────────────────────────────────────────

  onDragEnter(e: DragEvent): void {
    e.preventDefault();
    if (++this.dragCount === 1) this.isDragging.set(true);
  }

  onDragOver(e: DragEvent): void {
    e.preventDefault();
  }

  onDragLeave(): void {
    if (--this.dragCount === 0) this.isDragging.set(false);
  }

  onDrop(e: DragEvent): void {
    e.preventDefault();
    this.dragCount = 0;
    this.isDragging.set(false);
    const file = e.dataTransfer?.files[0];
    if (file) this.uploadFile(file);
  }

  // ── Syncfusion uploader callbacks ────────────────────────────────────────

  onRagUploadComplete(event: { fileName: string }): void {
    this.uploadError.set(null);
    this.documents.set([
      { id: '', fileName: event.fileName, status: 'Processing', chunkCount: 0 },
      ...this.documents()
    ]);
    this.startPolling();
  }

  onUploadError(msg: string): void {
    this.uploadError.set(msg);
  }

  // ── Internal ─────────────────────────────────────────────────────────────

  private uploadFile(file: File): void {
    if (!file.name.toLowerCase().endsWith('.pdf')) {
      this.uploadError.set('Only PDF files are supported.');
      return;
    }
    if (file.size > 50_000_000) {
      this.uploadError.set('File exceeds the 50 MB limit.');
      return;
    }

    this.uploading.set(true);
    this.uploadError.set(null);

    this.service.upload(file).subscribe({
      next: res => {
        this.uploading.set(false);
        this.documents.set([
          { id: res.id, fileName: res.fileName, status: 'Processing', chunkCount: 0 },
          ...this.documents()
        ]);
        this.startPolling();
      },
      error: () => {
        this.uploading.set(false);
        this.uploadError.set('Upload failed. Please try again.');
      }
    });
  }

  // ── Conflict resolution ──────────────────────────────────────────────────

  resolve(doc: DocumentStatus, action: ResolveAction): void {
    this.resolving.set(true);
    // replace needs IDs to supersede; add-to-series needs IDs to group into the same series
    const replaceIds = (action === 'replace' || action === 'add-to-series')
      ? (doc.conflicts ?? []).map(c => c.documentId)
      : undefined;

    this.service.resolve(doc.id, action, replaceIds).subscribe({
      next: () => {
        this.resolving.set(false);
        this.loadDocuments();
        if (action !== 'cancel') this.startPolling();
      },
      error: () => {
        this.resolving.set(false);
        this.uploadError.set('Could not resolve conflict. Please try again.');
      }
    });
  }

  similarityLabel(sim: number): string {
    return `${Math.round(sim * 100)}% match`;
  }

  similarityClass(sim: number): string {
    if (sim >= 0.85) return 'sim-high';
    if (sim >= 0.65) return 'sim-med';
    return 'sim-low';
  }

  // ── Internal ─────────────────────────────────────────────────────────────

  private startPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = interval(4000)
      .pipe(switchMap(() => this.service.list()))
      .subscribe({
        next: docs => {
          this.documents.set(docs);
          if (!docs.some(d => d.status === 'Processing')) this.pollSub?.unsubscribe();
        }
      });
  }

  formatDate(dateStr?: string): string {
    if (!dateStr) return '';
    return new Date(dateStr).toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' });
  }

  chunks(n: number): string {
    return `${n} chunk${n !== 1 ? 's' : ''}`;
  }
}
