namespace Lumen.Eval;

public class EvalSettings
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5119/";
    public string EvalSetPath { get; set; } = "eval-set.json";
    public string RunsPath { get; set; } = "runs";
    public Dictionary<string, TenantCredentials> Tenants { get; set; } = [];
    public JudgeSettings Judge { get; set; } = new();
    public Dictionary<string, ModelPrice> Prices { get; set; } = [];
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
