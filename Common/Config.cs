using Microsoft.Extensions.Configuration;

namespace Claw0.Common;

/// <summary>
/// 配置管理 - 从 appsettings.json 和环境变量加载
/// </summary>
public class Config
{
    private readonly IConfiguration _configuration;

    public Config()
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    public string DeepSeekApiKey => _configuration["DEEPSEEK_API_KEY"] 
        ?? throw new InvalidOperationException("DEEPSEEK_API_KEY not set.");
    
    public string DeepSeekBaseUrl => _configuration["DEEPSEEK_BASE_URL"] ?? "https://api.deepseek.com/v1";
    
    public string ModelId => _configuration["MODEL_ID"] ?? "deepseek-chat";
    
    public string WorkspaceDir => _configuration["WORKSPACE_DIR"] 
        ?? Path.Combine(Directory.GetCurrentDirectory(), "workspace");
}
