import { Component, OnInit, inject } from '@angular/core';
import { ChatComponent } from './chat/chat.component';
import { SidebarComponent } from './sidebar/sidebar.component';
import { DocumentsComponent } from './documents/documents.component';
import { ChatService } from './chat/chat.service';
import { ThemeService } from './theme.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [ChatComponent, SidebarComponent, DocumentsComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit {
  private chatService = inject(ChatService);
  readonly theme = inject(ThemeService);

  chatKey = 'init';
  activeSessionId: string | null = null;
  sidebarRefresh = 0;
  sidebarOpen = true;
  view: 'chat' | 'documents' = 'chat';

  ngOnInit(): void {
    const saved = this.chatService.getSessionId();
    this.activeSessionId = saved;
    this.chatKey = saved ?? crypto.randomUUID();
  }

  selectSession(id: string): void {
    this.chatService.setSessionId(id);
    this.activeSessionId = id;
    this.chatKey = id;
  }

  newChat(): void {
    this.chatService.clearSession();
    this.activeSessionId = null;
    this.chatKey = crypto.randomUUID();
  }

  onSessionCreated(id: string): void {
    this.activeSessionId = id;
    this.sidebarRefresh++;
  }

  onSessionDeleted(id: string): void {
    if (this.activeSessionId === id) {
      this.newChat();
    }
  }

  toggleSidebar(): void {
    this.sidebarOpen = !this.sidebarOpen;
  }
}