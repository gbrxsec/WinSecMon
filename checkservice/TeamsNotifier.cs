using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace checkservice;

/// <summary>Envia alertas para Teams, Discord ou um endpoint HTTP compatível.</summary>
public class TeamsNotifier
{
    private readonly ILogger<TeamsNotifier> _logger;
    private readonly HttpClient _httpClient = new();
    private readonly string _webhookUrl;
    private readonly string _provider;
    private readonly string _serverName;
    private readonly string _companyName;

    public TeamsNotifier(ILogger<TeamsNotifier> logger, IConfiguration configuration)
    {
        _logger = logger;
        _webhookUrl = configuration["WEBHOOK_URL"]
            ?? configuration["Webhook:Url"]
            ?? string.Empty;
        _provider = (configuration["WEBHOOK_PROVIDER"]
            ?? configuration["Webhook:Provider"]
            ?? "generic").Trim().ToLowerInvariant();
        _serverName = configuration.GetValue<string>("ServerInfo:ServerName", Environment.MachineName);
        _companyName = configuration.GetValue<string>("ServerInfo:CompanyName", "Não informado");

        if (string.IsNullOrWhiteSpace(_webhookUrl))
            _logger.LogWarning("Webhook não configurado. Os alertas serão registrados apenas no log.");
    }

    public Task SendStartupNotificationAsync(string serverName, string companyName) => SendAsync(
        "🟢 Monitoramento iniciado", $"O WinSecMon iniciou no servidor {serverName}.", "00AA00",
        ("Empresa", companyName), ("Servidor", serverName));

    public Task SendFileIntegrityAlertAsync(string fileName, string filePath, string oldHash, string newHash) => SendAsync(
        $"🟣 Integridade alterada: {fileName}", $"O arquivo {fileName} foi alterado. Um backup do estado anterior foi criado.", "800080",
        ("Servidor", _serverName), ("Empresa", _companyName), ("Arquivo", fileName), ("Caminho", filePath),
        ("Hash anterior", ShortHash(oldHash)), ("Hash atual", ShortHash(newHash)));

    public Task SendNotificationAsync(string serviceName, bool isRunning, string statusDetail = "", bool isInitialCheck = false)
    {
        var title = isRunning ? $"✅ Serviço restaurado: {serviceName}" : $"❌ Serviço indisponível: {serviceName}";
        var message = isRunning
            ? $"O serviço {serviceName} voltou a funcionar."
            : isInitialCheck ? $"O serviço {serviceName} já estava indisponível no início do monitoramento."
            : $"O serviço {serviceName} parou de funcionar. Ação imediata necessária.";
        return SendAsync(title, message, isRunning ? "00AA00" : "CC0000",
            ("Servidor", _serverName), ("Empresa", _companyName), ("Serviço", serviceName),
            ("Status", isRunning ? "Online" : "Offline"), ("Detalhe", statusDetail));
    }

    public Task SendNewRdpSessionAlertAsync(RdpSession session) => SendAsync(
        $"🟢 Nova conexão RDP: {session.UserName}", $"Uma sessão RDP foi aberta no servidor {_serverName}.", "00AA00",
        ("Servidor", _serverName), ("Empresa", _companyName), ("Usuário", session.UserName),
        ("Cliente", session.ClientHostname), ("IP", session.ClientIp), ("Sessão", session.SessionId.ToString()));

    public Task SendRdpSessionLogoffAlertAsync(RdpSession session) => SendAsync(
        $"🟠 Logoff RDP: {session.UserName}", $"Uma sessão RDP foi encerrada no servidor {_serverName}.", "FF8800",
        ("Servidor", _serverName), ("Empresa", _companyName), ("Usuário", session.UserName),
        ("Cliente", session.ClientHostname), ("IP", session.ClientIp), ("Sessão", session.SessionId.ToString()));

    public Task SendHealthAlertAsync(string metricName, double currentValue, double threshold) => SendAsync(
        $"⚠️ Uso elevado de {metricName}", $"A métrica {metricName} atingiu {currentValue:F2}%, acima do limite de {threshold:F2}%.", "FF8800",
        ("Servidor", _serverName), ("Empresa", _companyName), ("Métrica", metricName),
        ("Uso atual", $"{currentValue:F2}%"), ("Limite", $"{threshold:F2}%"));

    private async Task SendAsync(string title, string message, string color, params (string Name, string Value)[] facts)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl)) return;
        if (!Uri.TryCreate(_webhookUrl, UriKind.Absolute, out var uri))
        {
            _logger.LogError("A URL do webhook configurada é inválida.");
            return;
        }

        var json = BuildPayload(title, message, color, facts);
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(uri, content);
            if (!response.IsSuccessStatusCode)
                _logger.LogError("Falha ao enviar alerta pelo webhook. Status HTTP: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar alerta pelo webhook.");
        }
    }

    private string BuildPayload(string title, string message, string color, (string Name, string Value)[] facts)
    {
        if (_provider == "discord")
            return JsonSerializer.Serialize(new { content = $"**{title}**\n{message}\n{FormatFacts(facts)}" });

        if (_provider == "teams" || _provider == "msteams")
        {
            var payload = new
            {
                @type = "MessageCard", @context = "http://schema.org/extensions", summary = title,
                themeColor = color, title, text = message,
                sections = new[] { new { facts = facts.Select(f => new { name = f.Name, value = f.Value }) } }
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        }

        return JsonSerializer.Serialize(new { title, message, server = _serverName, company = _companyName,
            timestamp = DateTimeOffset.Now, facts = facts.ToDictionary(f => f.Name, f => f.Value) });
    }

    private static string FormatFacts(IEnumerable<(string Name, string Value)> facts) =>
        string.Join("\n", facts.Select(f => $"• **{f.Name}:** {f.Value}"));

    private static string ShortHash(string hash) => hash.Length > 10 ? hash[..10] + "..." : hash;
}
