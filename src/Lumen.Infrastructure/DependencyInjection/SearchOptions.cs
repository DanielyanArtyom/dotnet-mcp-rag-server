namespace Lumen.Infrastructure.DependencyInjection;

public class SearchOptions
{
    public const string SectionName = "Search";

    public SearchMode Mode { get; set; } = SearchMode.Vector;
    public int RrfK { get; set; } = 60;
    public int Candidates { get; set; } = 50;
    public double LexicalWeight { get; set; } = 1.0;
    public double VectorWeight { get; set; } = 1.0;
    public int? MaxChunksPerDocument { get; set; }
}
