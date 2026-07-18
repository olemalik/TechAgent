import {
  Component, Input, Output, EventEmitter, ViewChild, ElementRef,
  signal, computed
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FileUploaderComponent, ChatAttachment } from '../../shared/file-uploader/file-uploader.component';

export interface SendEvent {
  text: string;
  attachment: ChatAttachment | null;
}

@Component({
  selector: 'app-chat-input',
  standalone: true,
  imports: [CommonModule, FileUploaderComponent],
  templateUrl: './chat-input.component.html',
  styleUrl: './chat-input.component.css'
})
export class ChatInputComponent {
  @Input() streamingDisabled = false;

  @Output() messageSend = new EventEmitter<SendEvent>();

  @ViewChild('textarea') textarea!: ElementRef<HTMLTextAreaElement>;
  @ViewChild(FileUploaderComponent) fileUploader!: FileUploaderComponent;

  text      = signal('');
  uploading = signal(false);

  canSend = computed(() =>
    (this.text().trim().length > 0 || !!this.fileUploader?.pendingFile()) &&
    !this.streamingDisabled &&
    !this.uploading()
  );

  onInput(event: Event): void {
    this.text.set((event.target as HTMLTextAreaElement).value);
  }

  onEnterKey(event: Event): void {
    if ((event as KeyboardEvent).shiftKey) return;
    event.preventDefault();
    this.submit();
  }

  /** Called by parent when user drops a file anywhere on the chat container */
  acceptDrop(file: File): void {
    this.fileUploader?.attachExternal(file);
  }

  submit(): void {
    if (!this.canSend()) return;

    const text        = this.text().trim();
    const pendingFile = this.fileUploader?.pendingFile() ?? null;

    this.text.set('');
    if (this.textarea) this.textarea.nativeElement.value = '';

    if (pendingFile) {
      this.uploading.set(true);
      this.fileUploader.uploadPending().then(attachment => {
        this.uploading.set(false);
        this.messageSend.emit({ text, attachment });
      });
    } else {
      this.messageSend.emit({ text, attachment: null });
    }
  }
}
