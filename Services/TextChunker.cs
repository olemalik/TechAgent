using System.Text;
using System.Text.RegularExpressions;

namespace OilGasAI.API.Services;

/// <summary>
/// Sentence-aware "Small-to-Big" chunker.
///
/// WHY TWO SIZES?
///   Small child chunk (150 words):  embedded in Qdrant → precise, low-noise vector search.
///   Large parent chunk (600 words): stored in PostgreSQL → rich context for the LLM answer.
///
/// WHY SENTENCE-AWARE?
///   Word-count splitting cuts mid-sentence, producing ambiguous, noisy embeddings.
///   Sentence boundaries give the embedding model clean, complete ideas to represent.
/// </summary>
public static partial class TextChunker
{
    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z])", RegexOptions.Compiled)]
    private static partial Regex SentenceBreak();

    private const int ChildWords = 150;
    private const int ParentWords = 600;
    private const int MinWords = 15;

    public sealed record ChunkPair(string ChildText, string ParentText, int ChunkIndex);

    public static IReadOnlyList<ChunkPair> CreatePairs(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var sentences = SentenceBreak()
            .Split(text.Trim())
            .Select(s => s.Trim())
            .Where(s => WordCount(s) >= 3)
            .ToArray();

        if (sentences.Length == 0) return [];

        var childChunks = GroupIntoChunks(sentences, ChildWords);
        var results = new List<ChunkPair>(childChunks.Count);

        for (int i = 0; i < childChunks.Count; i++)
        {
            var child = childChunks[i];
            var parent = BuildParent(childChunks, i, ParentWords);
            if (WordCount(child) >= MinWords)
                results.Add(new ChunkPair(child, parent, i));
        }

        return results;
    }

    private static List<string> GroupIntoChunks(string[] sentences, int targetWords)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        int words = 0;

        foreach (var s in sentences)
        {
            int sw = WordCount(s);
            if (words + sw > targetWords && words > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear(); words = 0;
            }
            current.Append(s).Append(' ');
            words += sw;
        }

        if (words >= MinWords) chunks.Add(current.ToString().Trim());
        return chunks;
    }

    private static string BuildParent(List<string> chunks, int center, int target)
    {
        var sb = new StringBuilder(chunks[center]);
        int words = WordCount(chunks[center]);
        int back = center - 1, fwd = center + 1;

        while (back >= 0 && words < target)
        {
            var candidate = chunks[back] + " " + sb;
            if (WordCount(candidate) > target) break;
            sb.Clear().Append(candidate);
            words = WordCount(sb.ToString());
            back--;
        }

        while (fwd < chunks.Count && words < target)
        {
            var candidate = sb + " " + chunks[fwd];
            if (WordCount(candidate) > target) break;
            sb.Clear().Append(candidate);
            words = WordCount(sb.ToString());
            fwd++;
        }

        return sb.ToString().Trim();
    }

    private static int WordCount(string s)
        => s.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
}