using Microsoft.ML.Tokenizers;
using Microsoft.SemanticKernel.Text;

namespace Lumen.Infrastructure.Chunking;

public class Chunker : IChunker
{
    private readonly int _maxTokensPerChunk;
    private readonly int _overlapTokens;
    private static readonly TiktokenTokenizer Tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    public Chunker(IOptions<ChunkingOptions> options)
    {
        _maxTokensPerChunk = options.Value.MaxTokensPerChunk;
        _overlapTokens = options.Value.OverlapTokens;
    }

    public List<DocumentChunk> Chunk(Guid documentId, List<DocumentElement> elements)
    {
        var chunks = new List<DocumentChunk>();
        var proseLines = new List<string>();
        var position = 0;

        void AddChunk(string text, ChunkType type)
        {
            // Running offsets are approximate: overlap and joiners make exact offsets into Document.Content impractical.
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = chunks.Count,
                Text = text,
                TokenCount = CountTokens(text),
                CharStart = position,
                CharEnd = position + text.Length,
                ChunkType = type
            });
            position += text.Length;
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

                foreach (var tableChunkText in SplitTableIfOversized(element.Text))
                    AddChunk(tableChunkText, ChunkType.Table);
                continue;
            }

            if (string.IsNullOrWhiteSpace(element.Text))
                continue;

            // Lines a quarter of the chunk budget let the paragraph packer combine several lines and still land under the budget.
            proseLines.AddRange(TextChunker.SplitPlainTextLines(element.Text, Math.Max(1, _maxTokensPerChunk / 4), CountTokens));
        }

        FlushProseRun();
        return chunks;
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

    private int CountTokens(string text) => Tokenizer.CountTokens(text);
}
