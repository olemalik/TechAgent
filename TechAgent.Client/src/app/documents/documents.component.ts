import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { DocumentService, DocumentStatus } from './document.service';
import { interval, Subscription } from 'rxjs';
import { switchMap } from 'rxjs/operators';

@Component({
  selector: 'app-documents',
  standalone: true,
  imports: [],
  templateUrl: './documents.component.html',
  styleUrl: './documents.component.css'
})
export class DocumentsComponent implements OnInit, OnDestroy {
  private service = inject(DocumentService);

  documents: DocumentStatus[] = [];
  uploading = false;
  uploadError: string | null = null;
  isDragOver = false;
  loadError = false;

  private pollSub?: Subscription;

  ngOnInit(): void {
    this.loadDocuments();
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  loadDocuments(): void {
    this.loadError = false;
    this.service.list().subscribe({
      next: docs => {
        this.documents = docs;
        if (docs.some(d => d.status === 'Processing')) {
          this.startPolling();
        }
      },
      error: () => (this.loadError = true)
    });
  }

  onDragOver(e: DragEvent): void {
    e.preventDefault();
    this.isDragOver = true;
  }

  onDragLeave(): void {
    this.isDragOver = false;
  }

  onDrop(e: DragEvent): void {
    e.preventDefault();
    this.isDragOver = false;
    const file = e.dataTransfer?.files[0];
    if (file) this.uploadFile(file);
  }

  onFileSelected(e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (file) this.uploadFile(file);
    (e.target as HTMLInputElement).value = '';
  }

  private uploadFile(file: File): void {
    if (!file.name.toLowerCase().endsWith('.pdf')) {
      this.uploadError = 'Only PDF files are supported.';
      return;
    }
    if (file.size > 50_000_000) {
      this.uploadError = 'File exceeds the 50 MB limit.';
      return;
    }

    this.uploading = true;
    this.uploadError = null;

    this.service.upload(file).subscribe({
      next: res => {
        this.uploading = false;
        this.documents.unshift({
          id: res.id,
          fileName: res.fileName,
          status: 'Processing',
          chunkCount: 0
        });
        this.startPolling();
      },
      error: () => {
        this.uploading = false;
        this.uploadError = 'Upload failed. Please try again.';
      }
    });
  }

  private startPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = interval(4000)
      .pipe(switchMap(() => this.service.list()))
      .subscribe({
        next: docs => {
          this.documents = docs;
          if (!docs.some(d => d.status === 'Processing')) {
            this.pollSub?.unsubscribe();
          }
        }
      });
  }

  formatDate(dateStr?: string): string {
    if (!dateStr) return '';
    return new Date(dateStr).toLocaleDateString([], {
      month: 'short', day: 'numeric', year: 'numeric'
    });
  }

  chunks(n: number): string {
    return `${n} chunk${n !== 1 ? 's' : ''}`;
  }
}
