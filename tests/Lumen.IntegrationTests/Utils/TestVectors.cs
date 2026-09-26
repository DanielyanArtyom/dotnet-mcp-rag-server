namespace Lumen.IntegrationTests.Utils;

public static class TestVectors
{
    // One-hot vectors are unit length, so cosine distance between them is well defined
    // (a zero vector would make <=> return NaN and quietly break ordering assertions).
    public static float[] OneHot(int index)
    {
        var vector = new float[EmbeddingConstants.Dimensions];
        vector[index] = 1f;
        return vector;
    }
}
