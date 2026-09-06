import { Component, Input, Output, EventEmitter, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ChatUIModule } from '@syncfusion/ej2-angular-interactive-chat';
import { MessageModel } from '@syncfusion/ej2-interactive-chat';
import { TECHAGENT_API_URL } from '../../tokens/api-url.token';

export interface AiMessage extends MessageModel {
  dbId?: number;
  feedbackScore?: number | null;
  attachment?: { name: string; url: string; contentType: string } | null;
}

export interface FeedbackEvent {
  msg: AiMessage;
  score: 1 | -1;
}

@Component({
  selector: 'ta-chat-messages',
  standalone: true,
  imports: [CommonModule, ChatUIModule],
  templateUrl: './chat-messages.component.html',
  styleUrl: './chat-messages.component.css'
})
export class ChatMessagesComponent {
  @Input() messages: AiMessage[] = [];
  @Input() isLoading   = false;
  @Input() hasMessages = false;
  @Input() currentUser = { id: 'user1', user: 'You' };
  @Input() aiUser      = { id: 'ai1',   user: 'TechAgent AI' };

  @Output() feedbackSubmit = new EventEmitter<FeedbackEvent>();

  readonly apiBase = inject(TECHAGENT_API_URL);

  get lastMsg(): AiMessage | null {
    return this.messages.length ? this.messages[this.messages.length - 1] : null;
  }

  get showFeedbackPrompt(): boolean {
    const m = this.lastMsg;
    return !this.isLoading && !!m && m.author?.id === 'ai1' && !!m['dbId'] && m['feedbackScore'] === null;
  }

  get showFeedbackDone(): boolean {
    const m = this.lastMsg;
    return !this.isLoading && !!m && m.author?.id === 'ai1' &&
      m['feedbackScore'] !== null && m['feedbackScore'] !== undefined;
  }

  sendFeedback(msg: AiMessage, score: 1 | -1): void {
    this.feedbackSubmit.emit({ msg, score });
  }

  isImage(contentType: string): boolean {
    return contentType?.startsWith('image/') ?? false;
  }
}