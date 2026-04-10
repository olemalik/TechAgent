using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly DailyJob _job;

    public TestController(DailyJob job)
    {
        _job = job;
    }

    [HttpGet("run")]
    public async Task<IActionResult> Run()
    {
        await _job.Run();
        return Ok("Triggered!");
    }
}