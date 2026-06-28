import { Component, OnInit, OnChanges, SimpleChanges, Input, Output, EventEmitter, inject } from '@angular/core';
import { ChatService, SessionSummary } from '../chat/chat.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.css'
})
export class SidebarComponent implements OnInit, OnChanges {
  @Input() activeSessionId: string | null = null;
  @Input() refreshSignal = 0;
  @Output() sessionSelected = new EventEmitter<string>();
  @Output() newChatRequested = new EventEmitter<void>();
  @Output() toggleRequested = new EventEmitter<void>();

  private chatService = inject(ChatService);

  sessions: SessionSummary[] = [];
  loadError = false;

  ngOnInit(): void {
    this.loadSessions();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['refreshSignal'] && !changes['refreshSignal'].firstChange) {
      this.loadSessions();
    }
  }

  loadSessions(): void {
    this.loadError = false;
    this.chatService.getSessions().subscribe({
      next: s => this.sessions = s,
      error: () => this.loadError = true
    });
  }

  formatDate(dateStr: string): string {
    const d = new Date(dateStr);
    const today = new Date();
    const isToday = d.toDateString() === today.toDateString();
    return isToday
      ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
      : d.toLocaleDateString([], { month: 'short', day: 'numeric' });
  }

  truncate(text: string, max = 38): string {
    return text.length > max ? text.slice(0, max) + '…' : text;
  }
}