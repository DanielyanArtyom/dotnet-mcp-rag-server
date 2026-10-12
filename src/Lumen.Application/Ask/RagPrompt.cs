using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lumen.Application.Ask;

public static class RagPrompt
{
    private static readonly Regex CitationMarker = new(@"\[[0-9]{1,3}(?:\s*,\s*[0-9]{1,3})*\]", RegexOptions.Compiled);

    private static readonly Regex SourceTag = new("<(/?)(source)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string HashWithoutContext = ComputeHash(PromptConstants.UserTemplateVersion);
    private static readonly string HashWithContext = ComputeHash(PromptConstants.UserTemplateVersionWithContext);

    public static string HashFor(bool sourceContextHeader) => sourceContextHeader ? HashWithContext : HashWithoutContext;

    public static string BuildUserMessage(string question, IReadOnlyList<ChunkSearchResult> sources, bool sourceContextHeader = false)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<sources>");

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            builder.Append("<source id=\"").Append(i + 1)
                .Append("\" title=\"").Append(EscapeAttribute(source.DocumentTitle))
                .Append("\" filed=\"").Append(source.DocumentFilingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append("\" kind=\"").Append(source.ChunkType.ToString().ToLowerInvariant()).Append('"');
            if (sourceContextHeader && source.ContextHeader is not null)
            {
                builder.Append(" context=\"").Append(EscapeAttribute(source.ContextHeader)).Append('"');
            }

            builder.AppendLine(">");
            builder.AppendLine(SourceTag.Replace(source.Text, "&lt;$1$2"));
            builder.AppendLine("</source>");
        }

        builder.AppendLine("</sources>");
        builder.AppendLine();
        builder.Append("Question: ").AppendLine(question);
        builder.AppendLine();
        builder.Append(PromptConstants.RulesReminder);

        return builder.ToString();
    }

    public static bool IsRefusal(string answer) =>
        string.Equals(CitationMarker.Replace(answer, string.Empty).Trim().TrimEnd('.').TrimEnd(), PromptConstants.RefusalSentence.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static string ComputeHash(string templateVersion) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            PromptConstants.SystemPrompt + "\n" + PromptConstants.RulesReminder + "\n" + templateVersion)))[..12];

    private static string EscapeAttribute(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
}
