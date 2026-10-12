namespace Lumen.Api.DTO.Responses;

public class SettingsDto
{
    public ChatSettingsDto Chat { get; set; } = new();
    public EmbeddingSettingsDto Embeddings { get; set; } = new();
    public ChunkingSettingsDto Chunking { get; set; } = new();
    public string SearchMode { get; set; } = default!;
    public SearchSettingsDto Search { get; set; } = new();
    public string IngestProfile { get; set; } = default!;
    public string PromptHash { get; set; } = default!;
    public McpSettingsDto Mcp { get; set; } = new();
}

public class ChatSettingsDto
{
    public string Provider { get; set; } = default!;
    public string Deployment { get; set; } = default!;
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public string? ReasoningEffort { get; set; }
    public int MaxOutputTokens { get; set; }
    public int ContextTopK { get; set; }
    public bool SourceContextHeader { get; set; }
}

public class EmbeddingSettingsDto
{
    public string Provider { get; set; } = default!;
    public string Model { get; set; } = default!;
}

public class ChunkingSettingsDto
{
    public int MaxTokensPerChunk { get; set; }
    public int OverlapTokens { get; set; }
    public bool TableCells { get; set; }
    public bool TableHeaders { get; set; }
    public bool TableCaptions { get; set; }
    public string TableFormat { get; set; } = default!;
}

public class SearchSettingsDto
{
    public string Mode { get; set; } = default!;
    public int RrfK { get; set; }
    public int Candidates { get; set; }
    public double LexicalWeight { get; set; }
    public double VectorWeight { get; set; }
    public int? MaxChunksPerDocument { get; set; }
}

public class McpSettingsDto
{
    public bool ContextTool { get; set; }
}
