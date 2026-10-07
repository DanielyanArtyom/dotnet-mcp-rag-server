using Microsoft.ML.Tokenizers;
using Microsoft.SemanticKernel.Text;

namespace Lumen.Infrastructure.Chunking;

public class Chunker : IChunker
{
    private readonly int _maxTokensPerChunk;
    private readonly int _overlapTokens;
    private readonly bool _contextHeaders;
    private readonly int _mergeBelowTokens;
    private readonly bool _protectDecimals;
    private static readonly TiktokenTokenizer Tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    // TextChunker splits long lines on '.', including the one inside "10.7", and rejoins the halves with a
    // space or newline ("$10. 7 billion"). Swapping the decimal point for a non-delimiter character before
    // splitting, and back afterwards, keeps numbers whole; tokens are always counted on the restored text.
    private const char DecimalPlaceholder = '\u2024';
    private static readonly Regex DecimalPoint = new(@"(?<=\d)\.(?=\d)", RegexOptions.Compiled);

    public Chunker(IOptions<ChunkingOptions> options)
    {
        _maxTokensPerChunk = options.Value.MaxTokensPerChunk;
        _overlapTokens = options.Value.OverlapTokens;
        _contextHeaders = options.Value.ContextHeaders;
        _mergeBelowTokens = options.Value.MergeBelowTokens;
        _protectDecimals = options.Value.ProtectDecimals;
    }

    public List<DocumentChunk> Chunk(Guid documentId, List<DocumentElement> elements, ChunkingContext context)
    {
        var chunks = new List<DocumentChunk>();
        var proseLines = new List<string>();
        var position = 0;
        string? section = null;

        void AddChunk(string text, ChunkType type)
        {
            var restored = Restore(text);
            // Running offsets are approximate: overlap and joiners make exact offsets into Document.Content impractical.
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = chunks.Count,
                Text = restored,
                TokenCount = CountTokens(restored),
                CharStart = position,
                CharEnd = position + restored.Length,
                ChunkType = type,
                ContextHeader = _contextHeaders ? ContextHeaderBuilder.Build(context, section) : null
            });
            position += restored.Length;
        }

        void FlushProseRun()
        {
            if (proseLines.Count == 0)
                return;

            var paragraphs = TextChunker.SplitPlainTextParagraphs(proseLines, _maxTokensPerChunk, _overlapTokens, null, CountTokens);
            foreach (var paragraph in paragraphs)
                AddChunk(paragraph, ChunkType.Prose);

            proseLines.Clear();
        }

        foreach (var element in elements)
        {
            if (element.Type == ElementType.Table)
            {
                FlushProseRun();
                if (string.IsNullOrWhiteSpace(element.Text))
                    continue;

                foreach (var tableChunkText in SplitTableIfOversized(Protect(element.Text)))
                    AddChunk(tableChunkText, ChunkType.Table);
                continue;
            }

            if (string.IsNullOrWhiteSpace(element.Text))
                continue;

            if (_contextHeaders && (element.Type == ElementType.Heading || SectionHeading.IsHeadingLike(element.Text)))
            {
                FlushProseRun();
                section = element.Text.Trim();
            }

            // Lines a quarter of the chunk budget let the paragraph packer combine several lines and still land under the budget.
            proseLines.AddRange(TextChunker.SplitPlainTextLines(Protect(element.Text), Math.Max(1, _maxTokensPerChunk / 4), CountTokens));
        }

        FlushProseRun();
        chunks = EnforceBudget(chunks);
        return _mergeBelowTokens > 0 ? MergeShortChunks(chunks) : chunks;
    }

    // SplitPlainTextParagraphs does not guarantee the budget when overlap is 0 (it can overshoot by dozens of
    // tokens), so any over-budget chunk is re-split here; chunks within budget pass through untouched.
    private List<DocumentChunk> EnforceBudget(List<DocumentChunk> chunks)
    {
        if (chunks.All(c => c.TokenCount <= _maxTokensPerChunk))
        {
            return chunks;
        }

        var result = new List<DocumentChunk>();
        foreach (var chunk in chunks)
        {
            if (chunk.TokenCount <= _maxTokensPerChunk)
            {
                result.Add(chunk);
                continue;
            }

            var offset = chunk.CharStart;
            foreach (var piece in RepackWithinBudget(chunk.Text))
            {
                result.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = chunk.DocumentId,
                    Text = piece,
                    TokenCount = CountTokens(piece),
                    CharStart = offset,
                    CharEnd = offset + piece.Length,
                    ChunkType = chunk.ChunkType,
                    ContextHeader = chunk.ContextHeader
                });
                offset += piece.Length;
            }
        }

        for (var i = 0; i < result.Count; i++)
        {
            result[i].ChunkIndex = i;
        }

        return result;
    }

    private IEnumerable<string> RepackWithinBudget(string text)
    {
        var current = new StringBuilder();
        foreach (var piece in TextChunker.SplitPlainTextLines(text, _maxTokensPerChunk, CountTokens).SelectMany(HardSplit))
        {
            if (current.Length > 0 && CountTokens(current + "\n" + piece) > _maxTokensPerChunk)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(piece);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private IEnumerable<string> HardSplit(string text)
    {
        while (CountTokens(text) > _maxTokensPerChunk)
        {
            var cut = Tokenizer.GetIndexByTokenCount(text, _maxTokensPerChunk, out _, out _);
            if (cut <= 0 || cut >= text.Length)
            {
                break;
            }

            yield return text[..cut];
            text = text[cut..];
        }

        yield return text;
    }

    // A short chunk is a page footer, a page number or a one-row table fragment; on its own it competes for
    // top-K slots with near-zero information. It is absorbed by a neighbour only if the budget still holds.
    private List<DocumentChunk> MergeShortChunks(List<DocumentChunk> chunks)
    {
        var result = new List<DocumentChunk>();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (chunk.TokenCount > _mergeBelowTokens)
            {
                result.Add(chunk);
                continue;
            }

            if (result.Count > 0 && TryAppend(result[^1], chunk))
            {
                continue;
            }

            if (i + 1 < chunks.Count && TryPrepend(chunks[i + 1], chunk))
            {
                continue;
            }

            result.Add(chunk);
        }

        for (var i = 0; i < result.Count; i++)
        {
            result[i].ChunkIndex = i;
        }

        return result;
    }

    private bool TryAppend(DocumentChunk receiver, DocumentChunk shortChunk)
    {
        var text = receiver.Text + "\n" + shortChunk.Text;
        var tokens = CountTokens(text);
        if (tokens > _maxTokensPerChunk)
        {
            return false;
        }

        receiver.Text = text;
        receiver.TokenCount = tokens;
        receiver.CharEnd = shortChunk.CharEnd;
        return true;
    }

    private bool TryPrepend(DocumentChunk receiver, DocumentChunk shortChunk)
    {
        var text = shortChunk.Text + "\n" + receiver.Text;
        var tokens = CountTokens(text);
        if (tokens > _maxTokensPerChunk)
        {
            return false;
        }

        receiver.Text = text;
        receiver.TokenCount = tokens;
        receiver.CharStart = shortChunk.CharStart;
        return true;
    }

    private IEnumerable<string> SplitTableIfOversized(string tableText)
    {
        if (CountTokens(tableText) <= _maxTokensPerChunk)
        {
            yield return tableText;
            yield break;
        }

        var currentGroup = new List<string>();

        foreach (var row in tableText.Split('\n').SelectMany(SplitOversizedRow))
        {
            currentGroup.Add(row);
            if (currentGroup.Count > 1 && CountTokens(string.Join("\n", currentGroup)) > _maxTokensPerChunk)
            {
                currentGroup.RemoveAt(currentGroup.Count - 1);
                yield return string.Join("\n", currentGroup);
                currentGroup = [row];
            }
        }

        if (currentGroup.Count > 0)
            yield return string.Join("\n", currentGroup);
    }

    private IEnumerable<string> SplitOversizedRow(string row)
    {
        if (CountTokens(row) <= _maxTokensPerChunk)
            return [row];

        return TextChunker.SplitPlainTextLines(row, _maxTokensPerChunk, CountTokens);
    }

    private string Protect(string text) => _protectDecimals ? DecimalPoint.Replace(text, DecimalPlaceholder.ToString()) : text;

    private string Restore(string text) => _protectDecimals ? text.Replace(DecimalPlaceholder, '.') : text;

    private int CountTokens(string text) => Tokenizer.CountTokens(Restore(text));
}
