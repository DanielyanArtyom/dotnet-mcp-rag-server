using System.Security.Cryptography;

namespace Lumen.Infrastructure.Embeddings;

public class FakeEmbeddingService : IEmbeddingService
{
    public string ModelName => EmbeddingConstants.FakeModelName;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        IReadOnlyList<float[]> vectors = texts.Select(Compute).ToList();
        return Task.FromResult(vectors);
    }

    // Derived from a hash rather than Random: the same text must embed to the same vector across
    // processes and runtime versions, because integration tests assert on ranking, not just on shape.
    private static float[] Compute(string text)
    {
        var vector = new float[EmbeddingConstants.Dimensions];
        var textBytes = Encoding.UTF8.GetBytes(text);
        var block = new byte[textBytes.Length + sizeof(int)];
        textBytes.CopyTo(block, 0);

        var written = 0;
        var counter = 0;
        while (written < vector.Length)
        {
            BitConverter.TryWriteBytes(block.AsSpan(textBytes.Length), counter++);
            var hash = SHA256.HashData(block);

            for (var offset = 0; offset + sizeof(int) <= hash.Length && written < vector.Length; offset += sizeof(int))
            {
                vector[written++] = BitConverter.ToInt32(hash, offset) / (float)int.MaxValue;
            }
        }

        Normalize(vector);
        return vector;
    }

    private static void Normalize(float[] vector)
    {
        var norm = Math.Sqrt(vector.Sum(x => (double)x * x));
        if (norm == 0)
        {
            vector[0] = 1f;
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }
}
