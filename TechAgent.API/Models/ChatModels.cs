namespace OilGasAI.API.Models;

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<ChatMessage> History { get; set; } = new();
}

public class ChatMessage
{
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
}

public class ChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }
}

// Ollama API models
public class OllamaRequest
{
    public string Model { get; set; } = "oilgas-assistant";
    public List<OllamaMessage> Messages { get; set; } = new();
    public bool Stream { get; set; } = false; // No streaming
}

public class OllamaMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class OllamaResponse
{
    public OllamaMessage Message { get; set; } = new();
    public string Done { get; set; } = string.Empty;
}