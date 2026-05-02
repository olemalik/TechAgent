using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/telegram")]
public class TelegramController : ControllerBase
{
    private readonly TelegramService _telegram;
    private readonly DailyJob _job;

    public TelegramController(TelegramService telegram, DailyJob job)
    {
        _telegram = telegram;
    }

    [HttpGet("run")]
    public async Task<IActionResult> Run()
    {
        await _job.Run();
        return Ok("Triggered!");
    }

    [HttpPost("update")]
    public async Task<IActionResult> Update([FromBody] JsonElement update)
    {
        await _telegram.HandleUpdateAsync(update);
        return Ok();
    }
}
