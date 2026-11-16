using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace checkservice
{
    public class FileIntegrityChecker
    {
        private readonly ILogger<FileIntegrityChecker> _logger;
        private readonly TeamsNotifier _teamsNotifier;
        private readonly IConfiguration _configuration;
        private readonly Dictionary<string, string> _fileHashes;
        private readonly Dictionary<string, string> _shadowCopyPaths;
        private readonly List<FileMonitorConfig> _filesToMonitor;
        private readonly bool _isEnabled;

        public FileIntegrityChecker(ILogger<FileIntegrityChecker> logger, TeamsNotifier teamsNotifier, IConfiguration configuration)
        {
            _logger = logger;
            _teamsNotifier = teamsNotifier;
            _configuration = configuration;
            _fileHashes = new Dictionary<string, string>();
            _shadowCopyPaths = new Dictionary<string, string>();

            var configSection = _configuration.GetSection("FileIntegrityCheck");
            _isEnabled = configSection.GetValue<bool>("Enabled", false);
            _filesToMonitor = configSection.GetSection("FilesToMonitor").Get<List<FileMonitorConfig>>() ?? new List<FileMonitorConfig>();

            if (_isEnabled)
            {
                InitializeHashes();
                // Cria o diretório temporário para as cópias sombra
                Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "checkservice_shadow"));
            }
        }

        private void InitializeHashes()
        {
            foreach (var fileConfig in _filesToMonitor)
            {
                try
                {
                    var hash = CalculateFileHash(fileConfig.Path);
                    _fileHashes[fileConfig.Path] = hash;
                    _logger.LogInformation("Arquivo {FileName} inicializado com hash: {Hash}", fileConfig.Name, hash);

                    // Cria a cópia sombra
                    var shadowPath = CreateShadowCopy(fileConfig.Path);
                    _shadowCopyPaths[fileConfig.Path] = shadowPath;
                }
                catch (FileNotFoundException)
                {
                    _logger.LogError("Arquivo de configuração {FileName} não encontrado em {Path}.", fileConfig.Name, fileConfig.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao inicializar o hash do arquivo {FileName}.", fileConfig.Name);
                }
            }
        }

        public void CheckIntegrity()
        {
            if (!_isEnabled) return;

            foreach (var fileConfig in _filesToMonitor)
            {
                try
                {
                    var currentHash = CalculateFileHash(fileConfig.Path);
                    if (_fileHashes.TryGetValue(fileConfig.Path, out var initialHash))
                    {
                        if (currentHash != initialHash)
                        {
                            _logger.LogWarning("ALERTA DE INTEGRIDADE: Arquivo {FileName} foi modificado! Hash anterior: {InitialHash}, Hash atual: {CurrentHash}", fileConfig.Name, initialHash, currentHash);

                            // 1. Criar Backup (usando a cópia sombra, que é o estado anterior)
                            CreateBackup(fileConfig, _shadowCopyPaths[fileConfig.Path]);

                            // 2. Notificar Teams
                            _teamsNotifier.SendFileIntegrityAlertAsync(fileConfig.Name, fileConfig.Path, initialHash, currentHash).Wait();

                            // 3. Atualizar o hash e a cópia sombra para o novo estado
                            _fileHashes[fileConfig.Path] = currentHash;
                            UpdateShadowCopy(fileConfig.Path, _shadowCopyPaths[fileConfig.Path]);
                        }
                    }
                    else
                    {
                        // Arquivo não estava no dicionário, inicializa (pode acontecer se o arquivo for criado após o startup)
                        _fileHashes[fileConfig.Path] = currentHash;
                        _shadowCopyPaths[fileConfig.Path] = CreateShadowCopy(fileConfig.Path);
                    }
                }
                catch (FileNotFoundException)
                {
                    // Arquivo sumiu, notificar? Por enquanto, apenas logar.
                    _logger.LogError("Arquivo de configuração {FileName} não encontrado durante a checagem.", fileConfig.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao checar a integridade do arquivo {FileName}.", fileConfig.Name);
                }
            }
        }

        private string CalculateFileHash(string filePath)
        {
            // Lê o arquivo inteiro para garantir que o stream seja fechado e o arquivo liberado
            var fileBytes = File.ReadAllBytes(filePath);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(fileBytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private string CreateShadowCopy(string originalPath)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "checkservice_shadow");
            // Garante que o diretório exista
            Directory.CreateDirectory(tempDir);

            var shadowPath = Path.Combine(tempDir, Path.GetFileName(originalPath) + ".shadow");
            File.Copy(originalPath, shadowPath, true);
            return shadowPath;
        }

        private void UpdateShadowCopy(string originalPath, string shadowPath)
        {
            File.Copy(originalPath, shadowPath, true);
        }

        private void CreateBackup(FileMonitorConfig fileConfig, string sourceFilePath)
        {
            try
            {
                if (!Directory.Exists(fileConfig.BackupPath))
                {
                    Directory.CreateDirectory(fileConfig.BackupPath);
                }

                var backupFileName = $"{fileConfig.Name.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.bkp";
                var backupFilePath = Path.Combine(fileConfig.BackupPath, backupFileName);

                // Copia o arquivo da cópia sombra (que contém o estado anterior) para o backup final
                File.Copy(sourceFilePath, backupFilePath, true);
                _logger.LogInformation("Backup do arquivo {FileName} criado em {BackupPath}", fileConfig.Name, backupFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar backup do arquivo {FileName}.", fileConfig.Name);
            }
        }
    }

    public class FileMonitorConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
    }
}
