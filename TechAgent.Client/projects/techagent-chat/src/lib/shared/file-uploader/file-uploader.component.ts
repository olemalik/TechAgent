import {
  Component, Input, Output, EventEmitter, ViewChild, ElementRef, inject, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { UploaderComponent, UploaderModule } from '@syncfusion/ej2-angular-inputs';
import { HttpClient } from '@angular/common/http';
import { TECHAGENT_API_URL } from '../../tokens/api-url.token';

export interface ChatAttachment {
  name: string;
  url: string;
  contentType: string;
}

@Component({
  selector: 'ta-file-uploader',
  standalone: true,
  imports: [CommonModule, UploaderModule],
  templateUrl: './file-uploader.component.html',
  styleUrl: './file-uploader.component.css'
})
export class FileUploaderComponent {
  @Input() mode: 'rag' | 'chat' = 'rag';
  @Input() disabled = false;

  @Output() chatFileReady     = new EventEmitter<ChatAttachment>();
  @Output() ragUploadComplete = new EventEmitter<{ fileName: string }>();
  @Output() uploadError       = new EventEmitter<string>();

  @ViewChild('chatFileInput') chatFileInput!: ElementRef<HTMLInputElement>;
  @ViewChild('uploader') uploader!: UploaderComponent;

  private readonly baseUrl = inject(TECHAGENT_API_URL);
  private readonly http    = inject(HttpClient);

  readonly ragSaveUrl      = `${this.baseUrl}/api/documents/upload`;
  readonly ragAllowed      = '.pdf';
  readonly ragMaxSize      = 50_000_000;
  readonly ragAsyncSettings = { saveUrl: this.ragSaveUrl };

  uploading = signal(false);
  errorMsg  = signal<string | null>(null);

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

  clearPending(): void { this.pendingFile.set(null); }

  attachExternal(file: File): void { this.pendingFile.set(file); }

  async uploadPending(): Promise<ChatAttachment | null> {
    const file = this.pendingFile();
    if (!file) return null;
    return new Promise(resolve => {
      const form = new FormData();
      form.append('file', file);
      this.http.post<ChatAttachment>(`${this.baseUrl}/api/file/upload`, form).subscribe({
        next: res => { this.pendingFile.set(null); resolve(res); },
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