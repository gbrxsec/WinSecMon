using checkservice;
using Microsoft.Extensions.Options;
using System.ServiceProcess;

namespace checkservice
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly TeamsNotifier _teamsNotifier;
        private readonly IConfiguration _configuration;
        private readonly FileIntegrityChecker _fileIntegrityChecker;
        private readonly SystemHealthChecker _systemHealthChecker;
        private readonly RdpSessionTracker _rdpSessionTracker;
        private readonly List<string> _servicesToCheck;
        private readonly Dictionary<string, bool> _serviceStatus;

        public Worker(ILogger<Worker> logger, TeamsNotifier teamsNotifier, IConfiguration configuration, FileIntegrityChecker fileIntegrityChecker, SystemHealthChecker systemHealthChecker, RdpSessionTracker rdpSessionTracker)
        {
            _logger = logger;
            _teamsNotifier = teamsNotifier;
            _configuration = configuration;
            _fileIntegrityChecker = fileIntegrityChecker;
            _systemHealthChecker = systemHealthChecker;
            _rdpSessionTracker = rdpSessionTracker;
            _servicesToCheck = _configuration.GetSection("ServiceChecker:Services").Get<List<string>>() ?? new List<string>();
            _serviceStatus = _servicesToCheck.Distinct().ToDictionary(s => s, s => false);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker Service started. Monitoring services: {Services}", string.Join(", ", _servicesToCheck));

            foreach (var serviceName in _servicesToCheck)
            {
                CheckServiceStatus(serviceName, isInitialCheck: true);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var serviceName in _servicesToCheck)
                {
                    CheckServiceStatus(serviceName);
                }

                _fileIntegrityChecker.CheckIntegrity();

                _systemHealthChecker.CheckHealth();

                var (newRdpSessions, disconnectedSessions) = _rdpSessionTracker.CheckForSessionChanges();

                foreach (var session in newRdpSessions)
                {
                    _logger.LogInformation("Nova sessão RDP detectada: Usuário={User}, IP={IP}, Hostname={Host}", session.UserName, session.ClientIp, session.ClientHostname);
                    _teamsNotifier.SendNewRdpSessionAlertAsync(session).Wait();
                }

                foreach (var session in disconnectedSessions)
                {
                    _logger.LogInformation("Sessão RDP desconectada: Usuário={User}, IP={IP}, Hostname={Host}", session.UserName, session.ClientIp, session.ClientHostname);
                    _teamsNotifier.SendRdpSessionLogoffAlertAsync(session).Wait();
                }
                var checkIntervalSeconds = _configuration.GetValue<int>("ServiceChecker:CheckIntervalSeconds", 10);
                await Task.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), stoppingToken);
            }
        }


        private void CheckServiceStatus(string serviceName, bool isInitialCheck = false)
        {
            try
            {
                using var serviceController = new ServiceController(serviceName);
                serviceController.Refresh(); 
                var currentStatus = serviceController.Status;
                var isRunning = currentStatus == ServiceControllerStatus.Running;
                var isPending = currentStatus == ServiceControllerStatus.StartPending || currentStatus == ServiceControllerStatus.StopPending;
                var wasRunning = _serviceStatus[serviceName];

                if (isInitialCheck)
                {
                    if (!isRunning)
                    {
                        _logger.LogError("Service {ServiceName} is STOPPED on startup. Status: {Status}", serviceName, currentStatus);
                        _teamsNotifier.SendNotificationAsync(serviceName, false, currentStatus.ToString(), isInitialCheck: true).Wait();
                        _serviceStatus[serviceName] = false;
                    }
                    else
                    {
                        _serviceStatus[serviceName] = true;
                    }
                    return;
                }

                if (isRunning && !wasRunning)
                {
                    _logger.LogInformation("Service {ServiceName} is now RUNNING.", serviceName);
                    _teamsNotifier.SendNotificationAsync(serviceName, true, currentStatus.ToString()).Wait();
                    _serviceStatus[serviceName] = true;
                }
                else if (isPending && wasRunning)
                {
                    _logger.LogWarning("Service {ServiceName} is PENDING! Status: {Status}", serviceName, currentStatus);
                    _teamsNotifier.SendNotificationAsync(serviceName, false, currentStatus.ToString()).Wait();
                    _serviceStatus[serviceName] = false; 
                }
                else if (!isRunning && wasRunning)
                {
                    _logger.LogError("Service {ServiceName} has STOPPED! Status: {Status}", serviceName, currentStatus);
                    _teamsNotifier.SendNotificationAsync(serviceName, false, currentStatus.ToString()).Wait();
                    _serviceStatus[serviceName] = false;
                }
                else if (!isRunning && !wasRunning)
                {
                    _logger.LogDebug("Service {ServiceName} is still STOPPED. Status: {Status}", serviceName, currentStatus);
                }
            }
            catch (InvalidOperationException ex)
            {
                if (_serviceStatus.ContainsKey(serviceName) && _serviceStatus[serviceName])
                {
                    _logger.LogError("Service {ServiceName} not found or access denied. Error: {Error}", serviceName, ex.Message);
                    _teamsNotifier.SendNotificationAsync(serviceName, false, "Service Not Found").Wait();
                    _serviceStatus[serviceName] = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking service {ServiceName}", serviceName);
            }
        }
    }
}
