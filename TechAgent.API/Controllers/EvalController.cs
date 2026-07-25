using Microsoft.AspNetCore.Mvc;
using OilGasAI.API.Models;
using OilGasAI.API.Services;
using System.Text.Json;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;

namespace OilGasAI.API.Controllers;

/// <summary>
/// POST /api/eval/run
///
/// Runs the golden evaluation set against the live RAG pipeline and returns
/// Recall@K and Faithfulness metrics.
///
/// Usage:
///   curl -X POST http://localhost:5073/api/eval/run \
///        -H "Content-Type: application/json" \
///        -d @TechAgent.API/Data/golden-set.json
///
/// Or omit the body to run the bundled golden-set.json from disk.
/// The report is also written to Data/eval-results-{timestamp}.json for comparison over time.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EvalController : ControllerBase
{
    private readonly EvalService _eval;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EvalController> _log;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public EvalController(EvalService eval, IWebHostEnvironment env, ILogger<EvalController> log)
    {
        _eval = eval;
        _env = env;
        _log = log;
    }

    /// <summary>
    /// POST /api/eval/run
    /// Body: JSON array of GoldenEntry objects.
    /// Omit body to load the bundled Data/golden-set.json.
    /// Returns: EvalReport with RecallAtK, AvgFaithfulness, and per-question results.
    /// </summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] List<GoldenEntry>? entries, CancellationToken ct)
    {
        if (entries is null || entries.Count == 0)
        {
            // Load bundled golden set from disk
            var path = IOPath.Combine(_env.ContentRootPath, "Data", "golden-set.json");
            if (!IOFile.Exists(path))
                return BadRequest("No golden set provided in request body and Data/golden-set.json not found.");

            var json = await IOFile.ReadAllTextAsync(path, ct);
            entries = JsonSerializer.Deserialize<List<GoldenEntry>>(json, _jsonOpts);

            if (entries is null || entries.Count == 0)
                return BadRequest("golden-set.json is empty or invalid.");
        }

        _log.LogInformation("Eval run started: {N} questions", entries.Count);

        var report = await _eval.RunAsync(entries, ct);

        // Persist the report for historical comparison
        await SaveReportAsync(report, ct);

        _log.LogInformation(
            "Eval run complete: Recall@{K}={R:P1}, Faithfulness={F:P1}, Errors={E}",
            report.TopK, report.RecallAtK, report.AvgFaithfulness, report.Errors);

        return Ok(report);
    }

    /// <summary>
    /// GET /api/eval/golden-set
    /// Returns the bundled example golden set so you can use it as a template.
    /// </summary>
    [HttpGet("golden-set")]
    public async Task<IActionResult> GetGoldenSet(CancellationToken ct)
    {
        var path = IOPath.Combine(_env.ContentRootPath, "Data", "golden-set.json");
        if (!IOFile.Exists(path))
            return NotFound("Data/golden-set.json not found. Add your golden questions there.");

        var json = await IOFile.ReadAllTextAsync(path, ct);
        return Content(json, "application/json");
    }

    private async Task SaveReportAsync(EvalReport report, CancellationToken ct)
    {
        try
        {
            var dir = IOPath.Combine(_env.ContentRootPath, "Data", "eval-results");
            Directory.CreateDirectory(dir);
            var fileName = $"eval-{report.RanAt:yyyyMMdd-HHmmss}.json";
            var json = JsonSerializer.Serialize(report, _jsonOpts);
            await IOFile.WriteAllTextAsync(IOPath.Combine(dir, fileName), json, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not persist eval report — results still returned to caller.");
        }
    }
}