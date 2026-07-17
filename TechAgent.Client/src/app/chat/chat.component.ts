import {
  Component, AfterViewInit, OnDestroy, ViewChild, ElementRef,
  Output, EventEmitter, inject, NgZone, ChangeDetectorRef, DestroyRef
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChatUIModule } from '@syncfusion/ej2-angular-interactive-chat';
import { MessageModel } from '@syncfusion/ej2-interactive-chat';
import { ChatService } from './chat.service';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [ChatUIModule],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.css'
})
export class ChatComponent implements AfterViewInit, OnDestroy {
  @ViewChild('messageInput') messageInput!: ElementRef<HTMLTextAreaElement>;
  @Output() sessionCreated = new EventEmitter<string>();

  private chatService = inject(ChatService);
  private ngZone = inject(NgZone);
  private cd = inject(ChangeDetectorRef);
  private destroyRef = inject(DestroyRef);

  readonly currentUser = { id: 'user1', user: 'You' };
  readonly aiUser   = { id: 'ai1',   user: 'TechAgent AI' };

  messages: MessageModel[] = [];
  isLoading   = false;
  hasMessages = false;
  inputHasText = false;

  private streamAbort?: AbortController;

  ngAfterViewInit(): void {
    if (!this.chatService.getSessionId()) return;

    this.chatService.getHistory()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: history => {
          if (!history.length) return;
          this.messages = history.map(h => ({
            text: h.message,
            author: h.role === 'user' ? this.currentUser : this.aiUser,
            timeStamp: new Date(h.createdAt)
          }));
          this.hasMessages = true;
        },
        error: () => this.chatService.clearSession()
      });
  }

  ngOnDestroy(): void {
    this.streamAbort?.abort();
  }

  onInput(event: Event): void {
    this.inputHasText = (event.target as HTMLTextAreaElement).value.trim().length > 0;
  }

  onEnterKey(event: Event): void {
    const ke = event as KeyboardEvent;
    if (ke.shiftKey) return;
    ke.preventDefault();
    this.sendMessage();
  }

  sendMessage(): void {
    const userText = this.messageInput.nativeElement.value.trim();
    if (!userText || this.isLoading) return;

    this.messageInput.nativeElement.value = '';
    this.inputHasText = false;
    this.hasMessages  = true;
    this.isLoading    = true;

    // Add user bubble + empty AI placeholder via the messages binding
    this.messages = [
      ...this.messages,
      { text: userText, author: this.currentUser, timeStamp: new Date() },
      { text: '',       author: this.aiUser,       timeStamp: new Date() }
    ];
    const aiIdx = this.messages.length - 1;

    this.streamAbort = new AbortController();

    // Run fetch outside Angular zone so CD doesn't fire on every read() call
    this.ngZone.runOutsideAngular(() => {
      this.chatService.sendStreaming(userText, this.streamAbort!.signal)
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
                    // Replace the AI placeholder with updated text
                    this.ngZone.run(() => {
                      const next = [...this.messages];
                      next[aiIdx] = { ...next[aiIdx], text: accumulated };
                      this.messages = next;
                      this.cd.detectChanges();
                    });

                  } else if (evt.type === 'done') {
                    this.ngZone.run(() => {
                      this.isLoading = false;
                      if (evt.sessionId) {
                        const isNew = !this.chatService.getSessionId();
                        this.chatService.setSessionId(evt.sessionId);
                        if (isNew) this.sessionCreated.emit(evt.sessionId);
                      }
                      this.cd.detectChanges();
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
    const next = [...this.messages];
    next[aiIdx] = {
      ...next[aiIdx],
      text: partial || 'Connection error. Please ensure the API server is running on port 5073.'
    };
    this.messages  = next;
    this.isLoading = false;
    this.cd.detectChanges();
  }
}