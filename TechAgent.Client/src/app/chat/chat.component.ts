import { Component, AfterViewInit, ViewChild, ElementRef, Output, EventEmitter, inject, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChatUIModule, ChatUIComponent } from '@syncfusion/ej2-angular-interactive-chat';
import { MessageModel } from '@syncfusion/ej2-interactive-chat';
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
  @ViewChild('messageInput') messageInput!: ElementRef<HTMLTextAreaElement>;
  @Output() sessionCreated = new EventEmitter<string>();

  private chatService = inject(ChatService);
  private destroyRef = inject(DestroyRef);

  readonly currentUser = { id: 'user1', user: 'You' };
  readonly aiUser = { id: 'ai1', user: 'TechAgent AI' };

  messages: MessageModel[] = [];
  isLoading = false;
  hasMessages = false;
  inputHasText = false;

  ngAfterViewInit(): void {
    if (!this.chatService.getSessionId()) return;

    this.chatService.getHistory()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (history) => {
          if (history.length > 0) this.hasMessages = true;
          history.forEach(h => {
            this.chatUI.addMessage({
              text: h.message,
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

  onInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.inputHasText = value.trim().length > 0;
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
    this.hasMessages = true;
    this.isLoading = true;

    this.chatUI.addMessage({
      text: userText,
      author: this.currentUser,
      timeStamp: new Date()
    });

    this.chatService.send(userText).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        if (response.sessionId) {
          const isNew = !this.chatService.getSessionId();
          this.chatService.setSessionId(response.sessionId);
          if (isNew) this.sessionCreated.emit(response.sessionId);
        }

        const replyText = response.isSuccess
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
