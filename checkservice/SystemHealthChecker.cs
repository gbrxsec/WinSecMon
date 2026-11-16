using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace checkservice
{
    public class SystemHealthChecker : IDisposable
    {
        private readonly ILogger<SystemHealthChecker> _logger;
        private readonly TeamsNotifier _teamsNotifier;
        private readonly IConfiguration _configuration;
        private bool _isEnabled;
        private readonly double _cpuThreshold;
        private readonly double _memoryThreshold;
        private readonly double _diskThreshold;
        private readonly string _diskDrive;

        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _memoryCounter;
        private PerformanceCounter? _diskCounter;

        public SystemHealthChecker(ILogger<SystemHealthChecker> logger, TeamsNotifier teamsNotifier, IConfiguration configuration)
        {
            try
            {
                _logger = logger;
                _teamsNotifier = teamsNotifier;
                _configuration = configuration;

                var configSection = _configuration.GetSection("SystemHealthCheck");
                _isEnabled = configSection.GetValue<bool>("Enabled", false);
                _cpuThreshold = configSection.GetValue<double>("CpuThreshold", 85.0);
                _memoryThreshold = configSection.GetValue<double>("MemoryThreshold", 85.0);
                _diskThreshold = configSection.GetValue<double>("DiskThreshold", 90.0);
                _diskDrive = configSection.GetValue<string>("DiskDrive", "C");

                if (_isEnabled && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    InitializeCounters();
                }
                else if (_isEnabled)
                {
                    _logger.LogWarning("Monitoramento de saúde do sistema está ativado, mas só é totalmente suportado no Windows. Desativando.");
                    _isEnabled = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro fatal durante a inicialização do SystemHealthChecker. O monitoramento de saúde do sistema será desativado.");
                _isEnabled = false;
            }
        }

        private void InitializeCounters()
        {

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao inicializar o contador de CPU. O monitoramento de CPU será ignorado.");
                _cpuCounter = null;
            }

            try
            {
                _memoryCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                _memoryCounter.NextValue();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao inicializar o contador de Memória. O monitoramento de Memória será ignorado.");
                _memoryCounter = null;
            }

            try
            {
                _diskCounter = new PerformanceCounter("LogicalDisk", "% Free Space", $"{_diskDrive}:");
                _diskCounter.NextValue(); // Inicializa
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao inicializar o contador de Disco para a unidade '{DiskDrive}'. O monitoramento de Disco será ignorado.", _diskDrive);
                _diskCounter = null;
            }
        }

        public void CheckHealth()
        {
            if (!_isEnabled) return;

            // CPU
            if (_cpuCounter != null)
            {
                try
                {
                    var cpuUsage = _cpuCounter.NextValue();
                    if (cpuUsage > _cpuThreshold)
                    {
                        _logger.LogWarning("ALERTA DE SAÚDE: Uso de CPU ({CpuUsage:F2}%) acima do limite ({CpuThreshold:F2}%)", cpuUsage, _cpuThreshold);
                        _teamsNotifier.SendHealthAlertAsync("CPU", cpuUsage, _cpuThreshold).Wait();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro em tempo de execução ao checar o uso de CPU.");
                }
            }

            // Memória
            if (_memoryCounter != null)
            {
                try
                {
                    var memoryUsage = _memoryCounter.NextValue();
                    if (memoryUsage > _memoryThreshold)
                    {
                        _logger.LogWarning("ALERTA DE SAÚDE: Uso de Memória ({MemoryUsage:F2}%) acima do limite ({MemoryThreshold:F2}%)", memoryUsage, _memoryThreshold);
                        _teamsNotifier.SendHealthAlertAsync("Memória", memoryUsage, _memoryThreshold).Wait();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro em tempo de execução ao checar o uso de Memória.");
                }
            }

            // Disco
            if (_diskCounter != null)
            {
                try
                {
                    // O contador retorna % de espaço livre, então o alerta é se o USO for maior que o limite
                    var freeDiskSpace = _diskCounter.NextValue();
                    var usedDiskSpace = 100.0 - freeDiskSpace;
                    if (usedDiskSpace > _diskThreshold)
                    {
                        _logger.LogWarning("ALERTA DE SAÚDE: Uso de Disco ({UsedDiskSpace:F2}%) acima do limite ({DiskThreshold:F2}%) na unidade {_diskDrive}:", usedDiskSpace, _diskThreshold, _diskDrive);
                        _teamsNotifier.SendHealthAlertAsync($"Disco ({_diskDrive}:)", usedDiskSpace, _diskThreshold).Wait();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro em tempo de execução ao checar o uso de Disco.");
                }
            }
        }

        // Adiciona IDisposable para liberar os PerformanceCounters
        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _diskCounter?.Dispose();
        }
    }
}
