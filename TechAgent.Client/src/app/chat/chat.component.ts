import {
  Component, OnInit, OnDestroy, Output, EventEmitter, ViewChild,
  signal, computed, inject, NgZone, DestroyRef
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChatService } from './chat.service';
import { ChatInputComponent, SendEvent } from './chat-input/chat-input.component';
import { ChatMessagesComponent, AiMessage, FeedbackEvent } from './chat-messages/chat-messages.component';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [ChatInputComponent, ChatMessagesComponent],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.css'
})
export class ChatComponent implements OnInit, OnDestroy {
  @Output() sessionCreated = new EventEmitter<string>();
  @ViewChild(ChatInputComponent) chatInput!: ChatInputComponent;

  private chatService = inject(ChatService);
  private ngZone      = inject(NgZone);
  private destroyRef  = inject(DestroyRef);

  readonly currentUser = { id: 'user1', user: 'You' };
  readonly aiUser      = { id: 'ai1',   user: 'TechAgent AI' };

  messages    = signal<AiMessage[]>([]);
  isLoading   = signal(false);
  isDragging  = signal(false);
  hasMessages = computed(() => this.messages().length > 0);

  private streamAbort?: AbortController;
  private dragCount = 0;

  ngOnInit(): void {
    if (!this.chatService.getSessionId()) return;
    this.chatService.getHistory()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: history => {
          if (!history.length) return;
          this.messages.set(history.map(h => ({
            text: h.message,
            author: h.role === 'user' ? this.currentUser : this.aiUser,
            timeStamp: new Date(h.createdAt),
            dbId: h.role === 'assistant' ? h.id : undefined,
            feedbackScore: h.feedbackScore ?? null,
            attachment: h.attachmentUrl
              ? { name: h.attachmentName!, url: h.attachmentUrl, contentType: h.attachmentContentType ?? '' }
              : null
          })));
        },
        error: () => this.chatService.clearSession()
      });
  }

  ngOnDestroy(): void {
    this.streamAbort?.abort();
  }

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
    if (file) this.chatInput?.acceptDrop(file);
  }

  onSend(event: SendEvent): void {
    this.startStream(event.text, event.attachment);
  }

  onFeedback(event: FeedbackEvent): void {
    const { msg, score } = event;
    if (!msg.dbId || msg.feedbackScore !== null) return;

    this.chatService.submitFeedback(msg.dbId, score).subscribe({
      next: () => {
        const idx = this.messages().indexOf(msg);
        if (idx === -1) return;
        const next = [...this.messages()];
        next[idx] = { ...next[idx], feedbackScore: score };
        this.messages.set(next);
      }
    });
  }

  private startStream(text: string, attachment: { name: string; url: string; contentType: string } | null): void {
    this.isLoading.set(true);

    const userMsg: AiMessage = {
      text,
      author: this.currentUser,
      timeStamp: new Date(),
      attachment
    };
    this.messages.set([...this.messages(), userMsg, { text: '', author: this.aiUser, timeStamp: new Date() }]);
    const aiIdx = this.messages().length - 1;

    this.streamAbort = new AbortController();

    this.ngZone.runOutsideAngular(() => {
      this.chatService.sendStreaming(text, this.streamAbort!.signal, attachment ?? undefined)
        .then(async reader => {
          let accumulated = '';
          let buffer      = '';
          const decoder   = new TextDecoder();

          try {
            while (true) {
              const { done, value } = await reader.read();
              if (done) break;

              buffer += decoder.decode(value, { stream: true });
              const parts = buffer.split('\n\n');
              buffer = parts.pop() ?? '';

              for (const part of parts) {
                const line = part.trim();
                if (!line.startsWith('data: ')) continue;
                try {
                  const evt = JSON.parse(line.slice(6));

                  if (evt.type === 'token' && evt.value) {
                    accumulated += evt.value;
                    this.ngZone.run(() => {
                      const next = [...this.messages()];
                      next[aiIdx] = { ...next[aiIdx], text: accumulated };
                      this.messages.set(next);
                    });

                  } else if (evt.type === 'done') {
                    this.ngZone.run(() => {
                      this.isLoading.set(false);
                      if (evt.assistantMessageId) {
                        const next = [...this.messages()];
                        next[aiIdx] = { ...next[aiIdx], dbId: evt.assistantMessageId, feedbackScore: null };
                        this.messages.set(next);
                      }
                      if (evt.sessionId) {
                        const isNew = !this.chatService.getSessionId();
                        this.chatService.setSessionId(evt.sessionId);
                        if (isNew) this.sessionCreated.emit(evt.sessionId);
                      }
                    });
                    return;
                  }
                } catch { /* skip malformed chunks */ }
              }
            }
          } catch (err: unknown) {
            if ((err as Error)?.name !== 'AbortError') {
              this.ngZone.run(() => this.onStreamError(aiIdx, accumulated));
            }
          }
        })
        .catch(() => this.ngZone.run(() => this.onStreamError(aiIdx, '')));
    });
  }

  private onStreamError(aiIdx: number, partial: string): void {
    const next = [...this.messages()];
    next[aiIdx] = {
      ...next[aiIdx],
      text: partial || 'Connection error. Please ensure the API server is running on port 5073.'
    };
    this.messages.set(next);
    this.isLoading.set(false);
  }
}
