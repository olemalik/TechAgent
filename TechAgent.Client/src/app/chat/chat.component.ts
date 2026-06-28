import { Component, AfterViewInit, ViewChild, Output, EventEmitter, inject, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChatUIModule, ChatUIComponent } from '@syncfusion/ej2-angular-interactive-chat';
import { MessageModel, MessageSendEventArgs } from '@syncfusion/ej2-interactive-chat';
import { ChatService } from './chat.service';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [ChatUIModule],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.css'
})
export class ChatComponent implements AfterViewInit {
  @ViewChild('chatUI') chatUI!: ChatUIComponent;
  @Output() sessionCreated = new EventEmitter<string>();

  private chatService = inject(ChatService);
  private destroyRef = inject(DestroyRef);

  readonly currentUser = { id: 'user1', user: 'You' };
  readonly aiUser = { id: 'ai1', user: 'TechAgent AI' };

  messages: MessageModel[] = [];
  isLoading = false;
  hasMessages = false;

  ngAfterViewInit(): void {
    if (!this.chatService.getSessionId()) return;

    this.chatService.getHistory()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (history) => {
          if (history.length > 0) this.hasMessages = true;
          history.forEach(h => {
            this.chatUI.addMessage({
              text: h.wasRefused && h.role === 'assistant'
                ? 'Sorry, that topic is outside my scope.'
                : h.message,
              author: h.role === 'user' ? this.currentUser : this.aiUser,
              timeStamp: new Date(h.createdAt)
            });
          });
        },
        error: () => {
          this.chatService.clearSession();
        }
      });
  }

  onMessageSend(args: MessageSendEventArgs): void {
    const userText = args.message?.text?.trim() ?? '';
    if (!userText || this.isLoading) return;

    this.hasMessages = true;
    this.isLoading = true;

    this.chatService.send(userText).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        if (response.sessionId) {
          const isNew = !this.chatService.getSessionId();
          this.chatService.setSessionId(response.sessionId);
          if (isNew) this.sessionCreated.emit(response.sessionId);
        }

        const replyText = response.wasRefused
          ? 'Sorry, that topic is outside my scope.'
          : response.isSuccess
            ? response.reply
            : (response.error ?? 'An error occurred. Please try again.');

        this.chatUI.addMessage({
          text: replyText,
          author: this.aiUser,
          timeStamp: new Date()
        });

        this.isLoading = false;
      },
      error: () => {
        this.chatUI.addMessage({
          text: 'Connection error. Please ensure the API server is running on port 5073.',
          author: this.aiUser,
          timeStamp: new Date()
        });
        this.isLoading = false;
      }
    });
  }
}
