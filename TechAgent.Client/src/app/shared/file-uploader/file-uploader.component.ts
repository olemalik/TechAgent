import {
  Component, Input, Output, EventEmitter, ViewChild, ElementRef, inject, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { UploaderComponent, UploaderModule } from '@syncfusion/ej2-angular-inputs';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export interface ChatAttachment {
  name: string;
  url: string;
  contentType: string;
}

/** Reusable file upload component.
 *
 *  mode='rag'  — Syncfusion ejs-uploader, PDF only, POSTs to /api/documents/upload
 *  mode='chat' — Compact paperclip button, any file, holds file until parent calls upload()
 */
@Component({
  selector: 'app-file-uploader',
  standalone: true,
  imports: [CommonModule, UploaderModule],
  templateUrl: './file-uploader.component.html',
  styleUrl: './file-uploader.component.css'
})
export class FileUploaderComponent {
  @Input() mode: 'rag' | 'chat' = 'rag';
  @Input() disabled = false;

  /** chat mode: emits once the file has been uploaded to /api/file/upload */
  @Output() chatFileReady = new EventEmitter<ChatAttachment>();

  /** rag mode: emits after each successful /api/documents/upload */
  @Output() ragUploadComplete = new EventEmitter<{ fileName: string }>();

  /** Both modes: emits on error */
  @Output() uploadError = new EventEmitter<string>();

  @ViewChild('chatFileInput') chatFileInput!: ElementRef<HTMLInputElement>;
  @ViewChild('uploader') uploader!: UploaderComponent;

  private http = inject(HttpClient);

  // ── RAG mode (Syncfusion) ────────────────────────────────────────────────

  readonly ragSaveUrl = `${environment.apiUrl}/api/documents/upload`;
  readonly ragAllowed = '.pdf';
  readonly ragMaxSize = 50_000_000; // 50 MB
  readonly ragAsyncSettings = { saveUrl: this.ragSaveUrl };

  uploading = signal(false);
  errorMsg  = signal<string | null>(null);

  // Called by Syncfusion before auto-upload starts
  onBeforeUpload(): void {
    this.uploading.set(true);
    this.errorMsg.set(null);
  }

  onRagSuccess(args: { file: { name: string } }): void {
    this.uploading.set(false);
    this.ragUploadComplete.emit({ fileName: args.file.name });
  }

  onRagFailure(args: { file: { name: string } }): void {
    this.uploading.set(false);
    const msg = `Upload failed for "${args.file.name}". Please try again.`;
    this.errorMsg.set(msg);
    this.uploadError.emit(msg);
  }

  // ── Chat mode (compact button) ───────────────────────────────────────────

  pendingFile = signal<File | null>(null);

  openPicker(): void {
    if (!this.disabled) this.chatFileInput.nativeElement.click();
  }

  onChatFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.pendingFile.set(file);
    (event.target as HTMLInputElement).value = '';
  }

  clearPending(): void {
    this.pendingFile.set(null);
  }

  /** Programmatically attach a file (e.g. from a drag-drop handler in the parent) */
  attachExternal(file: File): void {
    this.pendingFile.set(file);
  }

  /** Called by the parent (chat-input) when the user hits Send */
  async uploadPending(): Promise<ChatAttachment | null> {
    const file = this.pendingFile();
    if (!file) return null;

    return new Promise(resolve => {
      const form = new FormData();
      form.append('file', file);
      this.http.post<ChatAttachment>(`${environment.apiUrl}/api/file/upload`, form)
        .subscribe({
          next: res => {
            this.pendingFile.set(null);
            resolve(res);
          },
          error: () => {
            const msg = 'File upload failed. Please try again.';
            this.errorMsg.set(msg);
            this.uploadError.emit(msg);
            resolve(null);
          }
        });
    });
  }

  isImage(contentType: string): boolean {
    return contentType?.startsWith('image/') ?? false;
  }
}
