namespace OilGasAI.API.Models;

/// <summary>
/// One entry in the golden evaluation set.
/// The golden set is the ground truth used to measure Recall@4 and Faithfulness.
/// Store at: TechAgent.API/Data/golden-set.json
/// </summary>
public class GoldenEntry
{
    /// <summary>Human-readable ID for tracking which questions fail.</summary>
    public int Id { get; set; }

    /// <summary>Exactly what a real O&G engineer would type into the chat.</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// The name of the indexed PDF that contains the answer.
    /// Used to measure Recall@4 — was this document retrieved?
    /// </summary>
    public string SourceDocument { get; set; } = string.Empty;

    /// <summary>
    /// Key phrases that MUST appear in a correct answer (case-insensitive substring match).
    /// Minimum 2, aim for 3–5. Specific numbers and standards score best:
    ///   e.g. "5000 psi", "API 6A", "section 4.2"
    /// </summary>
    public List<string> KeyPhrases { get; set; } = [];

    /// <summary>
    /// Optional: a short human-written reference answer used for context.
    /// Not used in automated scoring — only for human review.
    /// </summary>
    public string? ReferenceAnswer { get; set; }
}

/// <summary>Result for one golden entry after running through the RAG pipeline.</summary>
public class EvalEntryResult
{
    public int Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public string SourceDocument { get; set; } = string.Empty;

    /// <summary>True if SourceDocument appeared in the top-K Qdrant results.</summary>
    public bool RecallHit { get; set; }

    /// <summary>Qdrant scores for the retrieved documents (highest first).</summary>
    public List<RetrievedDoc> Retrieved { get; set; } = [];

    /// <summary>Number of key phrases found in the actual AI answer.</summary>
    public int KeyPhrasesHit { get; set; }

    public int KeyPhrasesTotal { get; set; }

    /// <summary>Faithfulness = KeyPhrasesHit / KeyPhrasesTotal (0.0 – 1.0).</summary>
    public double Faithfulness => KeyPhrasesTotal == 0 ? 0 : (double)KeyPhrasesHit / KeyPhrasesTotal;

    public string ActualAnswer { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class RetrievedDoc
{
    public string FileName { get; set; } = string.Empty;
    public float Score { get; set; }
}

/// <summary>Aggregate metrics for the entire golden set run.</summary>
public class EvalReport
{
    public DateTimeOffset RanAt { get; set; } = DateTimeOffset.UtcNow;
    public int TotalQuestions { get; set; }
    public int Errors { get; set; }

    /// <summary>
    /// Recall@K: % of questions where the correct document appeared in the top-K results.
    /// Target: > 0.80 before considering fine-tuning.
    /// If this is low, fix retrieval — fine-tuning cannot help.
    /// </summary>
    public double RecallAtK { get; set; }

    /// <summary>
    /// Average faithfulness: % of key phrases present in AI answers.
    /// Target: > 0.70 once Recall@K is healthy.
    /// If Recall is high but this is low, generation is the bottleneck — consider RAFT.
    /// </summary>
    public double AvgFaithfulness { get; set; }

    public int TopK { get; set; }
    public float MinSimilarityScore { get; set; }
    public float DomainGuardThreshold { get; set; }

    public List<EvalEntryResult> Results { get; set; } = [];
}