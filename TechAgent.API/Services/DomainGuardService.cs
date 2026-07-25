namespace OilGasAI.API.Services;

/// <summary>
/// Two-layer domain guard — both must pass for a message to reach the AI.
///
/// LAYER 1 — Keyword fast check (microseconds):
///   Rejects obvious off-topic messages. If message has off-topic words AND no O&G words → reject.
///
/// LAYER 2 — Semantic similarity (milliseconds, ZERO extra Ollama calls):
///   Pre-computes a "centre point" of Oil &amp; Gas meaning on first use (averages 10 O&G phrases).
///   Compares the user's query embedding to this centre point.
///   Cosine similarity below 0.30 → reject (too far from O&G territory).
///   IMPORTANT: Reuses the embedding already generated for RAG retrieval — no wasted API call.
///
/// WHY NOT JUST KEYWORDS?
///   "What should the annular pressure be during gas injection?" has no obvious keywords.
///   The semantic layer catches these legitimate O&G questions correctly.
/// </summary>
public class DomainGuardService : IDomainGuardService
{
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<DomainGuardService> _log;

    private static readonly string[] OilGasKeywords = [
        "well","drill","drilling","reservoir","production","oil","gas","petroleum",
        "completion","frac","fracturing","hse","spill","pipeline","refinery",
        "upstream","downstream","midstream","casing","tubing","wellbore","log",
        "porosity","permeability","barrel","bbl","mcf","psi","seismic","offshore",
        "onshore","rig","mud","cement","perforation","artificial lift","esp","choke",
        "separator","compressor","lng","lpg","fpso","hydrocarbon","crude","bopd","gor","bop"
    ];

    private static readonly string[] OffTopicKeywords = [
        "recipe","cooking","movie","music","sport","football","cricket","politics",
        "religion","dating","fashion","gaming","homework","essay","poem","celebrity",
        "restaurant","travel","weather","finance","stock","crypto"
    ];

    private static readonly string[] DomainPhrases = [
        "search_query: drilling a well in an oil field",
        "search_query: reservoir engineering and production optimization",
        "search_query: HSE safety incident on offshore drilling platform",
        "search_query: pipeline flow assurance and corrosion inhibition",
        "search_query: refinery crude oil distillation process",
        "search_query: well completion perforating and stimulation fracturing",
        "search_query: blowout preventer BOP testing and maintenance procedure",
        "search_query: gas-oil ratio water cut production decline analysis",
        "search_query: seismic interpretation for hydrocarbon exploration",
        "search_query: LNG plant operations natural gas liquefaction"
    ];

    private float[]? _centroid;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly float _similarityThreshold;

    public DomainGuardService(IEmbeddingService embedding, IConfiguration config, ILogger<DomainGuardService> log)
    {
        _embedding = embedding;
        _log = log;
        // Tune against real O&G questions vs off-topic questions once you have a golden set.
        // Lower = more permissive; higher = stricter guard but more false refusals.
        _similarityThreshold = config.GetValue<float>("DomainGuard:SimilarityThreshold", 0.30f);
    }

    public async Task<bool> IsAllowedAsync(string message, float[]? queryEmbedding = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        var lower = message.ToLowerInvariant();

        // LAYER 1: fast keyword check
        bool offTopic = OffTopicKeywords.Any(k => lower.Contains(k));
        bool oilGas = OilGasKeywords.Any(k => lower.Contains(k));
        if (offTopic && !oilGas)
        {
            _log.LogDebug("Domain L1 blocked: '{M}'", message[..Math.Min(60, message.Length)]);
            return false;
        }

        // Short greetings / clarifications pass through
        if (message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3)
            return true;

        // LAYER 2: semantic similarity (only when we already have an embedding — zero extra cost)
        if (queryEmbedding is not null)
        {
            var centroid = await GetOrBuildCentroidAsync(ct);
            float sim = Dot(queryEmbedding, centroid);
            _log.LogDebug("Domain L2 sim={S:F3}", sim);
            if (sim < _similarityThreshold) { _log.LogInformation("Domain L2 blocked (sim={S:F3}, threshold={T:F3}).", sim, _similarityThreshold); return false; }
        }

        return true;
    }

    private async Task<float[]> GetOrBuildCentroidAsync(CancellationToken ct)
    {
        if (_centroid is not null) return _centroid;
        await _lock.WaitAsync(ct);
        try
        {
            if (_centroid is not null) return _centroid;
            _log.LogInformation("Building O&G domain centroid from {N} phrases.", DomainPhrases.Length);
            var vecs = await _embedding.EmbedBatchAsync(DomainPhrases, ct);
            _centroid = Normalize(Average(vecs));
            return _centroid;
        }
        finally { _lock.Release(); }
    }

    private static float[] Average(IReadOnlyList<float[]> vecs)
    {
        var avg = new float[vecs[0].Length];
        foreach (var v in vecs)
            for (int i = 0; i < avg.Length; i++) avg[i] += v[i] / vecs.Count;
        return avg;
    }

    private static float[] Normalize(float[] v)
    {
        double n = Math.Sqrt(v.Sum(x => (double)x * x));
        return n < 1e-10 ? v : v.Select(x => (float)(x / n)).ToArray();
    }

    private static float Dot(float[] a, float[] b)
    {
        float d = 0f;
        for (int i = 0; i < a.Length; i++) d += a[i] * b[i];
        return d;
    }
}