// Main component — drop <techagent-chat> anywhere in your app
export * from './lib/chat/chat.component';

// Token — provide your API URL in app.config.ts
export * from './lib/tokens/api-url.token';

// Models — useful if the consumer listens to (sessionCreated) or extends the chat
export * from './lib/chat/models/chat.model';
export * from './lib/chat/chat-messages/chat-messages.component';
export * from './lib/chat/chat-input/chat-input.component';