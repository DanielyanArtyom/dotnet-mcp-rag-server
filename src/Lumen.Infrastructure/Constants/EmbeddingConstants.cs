namespace Lumen.Infrastructure.Constants;

public static class EmbeddingConstants
{
    // The column type is vector(1536); a configurable dimension could silently disagree with the schema.
    public const int Dimensions = 1536;
    public const string FakeModelName = "fake-1536";
}
