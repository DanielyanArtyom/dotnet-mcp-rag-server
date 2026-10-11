namespace Lumen.Eval;

public class EvalSettings
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5119/";
    public string EvalSetPath { get; set; } = "eval-set.json";
    public string RunsPath { get; set; } = "runs";
    public Dictionary<string, TenantCredentials> Tenants { get; set; } = [];
    public JudgeSettings Judge { get; set; } = new();
    public Dictionary<string, ModelPrice> Prices { get; set; } = [];
    public AgentSettings Agent { get; set; } = new();
}

public class JudgeSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class ModelPrice
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
}

public class AgentSettings
{
    public string ClaudePath { get; set; } = "claude";
    public string Model { get; set; } = "sonnet";
    public int TimeoutSeconds { get; set; } = 180;
    public string? McpUrl { get; set; }
}
