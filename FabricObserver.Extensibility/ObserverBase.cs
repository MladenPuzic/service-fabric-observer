// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Fabric;
using System.Fabric.Health;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FabricObserver.Observers.Interfaces;
using FabricObserver.Observers.MachineInfoModel;
using FabricObserver.Observers.Utilities;
using FabricObserver.Observers.Utilities.Telemetry;
using ConfigSettings = FabricObserver.Observers.Utilities.ConfigSettings;
using HealthReport = FabricObserver.Observers.Utilities.HealthReport;

namespace FabricObserver.Observers
{
    public abstract class ObserverBase : IObserver
    {
        private const int TtlAddMinutes = 5;
        private const string FabricSystemAppName = "fabric:/System";
        private const int MaxDumps = 5;
        private readonly Dictionary<string, int> serviceDumpCountDictionary = new Dictionary<string, int>();
        private string SFLogRoot;
        private string dumpsPath;
        private bool disposed;

        public string ObserverName
        {
            get; set;
        }

        public bool IsObserverWebApiAppDeployed
        {
            get; set;
        }

        public string NodeName
        {
            get; set;
        }

        public string NodeType
        {
            get; set;
        }

        public ObserverHealthReporter HealthReporter
        {
            get;
        }

        public StatelessServiceContext FabricServiceContext
        {
            get;
        }

        public DateTime LastRunDateTime
        {
            get; set;
        }

        public TimeSpan RunDuration
        {
            get; set;
        }

        public CancellationToken Token
        {
            get; set;
        }

        public bool IsEnabled 
        { 
            get => ConfigurationSettings == null || ConfigurationSettings.IsEnabled;
            
            // default is observer enabled.
            set
            {
                if (ConfigurationSettings != null)
                {
                    ConfigurationSettings.IsEnabled = value;
                }
            }
        }

        public bool IsTelemetryEnabled
        {
            get
            {
                if (ConfigurationSettings != null)
                {
                    return IsTelemetryProviderEnabled && ConfigurationSettings.IsObserverTelemetryEnabled;
                }

                return false;
            }
        }

        public bool IsEtwEnabled
        {
            get
            {
                if (ConfigurationSettings != null)
                {
                    return ObserverLogger.EnableETWLogging && ConfigurationSettings.IsObserverEtwEnabled;
                }

                return false;
            }
        }

        public bool IsUnhealthy
        {
            get; set;
        }

        public ConfigSettings ConfigurationSettings
        {
            get; set;
        }

        public bool EnableVerboseLogging
        {
            get => ConfigurationSettings != null && ConfigurationSettings.EnableVerboseLogging;

            set
            {
                if (ConfigurationSettings != null)
                {
                    ConfigurationSettings.EnableVerboseLogging = value;
                }
            }
        }

        public bool EnableCsvLogging
        {
            get
            {
                if (ConfigurationSettings == null)
                {
                    return false;
                }

                if (CsvFileLogger == null && ConfigurationSettings.EnableCsvLogging)
                {
                    InitializeCsvLogger();
                }

                return ConfigurationSettings.EnableCsvLogging;
            }

            set
            {
                if (ConfigurationSettings != null)
                {
                    ConfigurationSettings.EnableCsvLogging = value;
                }
            }
        }

        /// <summary>
        /// The maximum number of days an archived observer log file will be stored. After this time, it will be deleted from disk.
        /// </summary>
        public int MaxLogArchiveFileLifetimeDays
        {
            get; set;
        }

        /// <summary>
        /// The maximum number of days a csv file produced by CsvLogger will be stored. After this time, it will be deleted from disk.
        /// </summary>
        public int MaxCsvArchiveFileLifetimeDays
        {
            get; set;
        }

        public Logger ObserverLogger
        {
            get; set;
        }

        public DataTableFileLogger CsvFileLogger
        {
            get; set;
        }

        // Each derived Observer can set this to maintain health status across iterations.
        public bool HasActiveFabricErrorOrWarning
        {
            get; set;
        }

        public List<string> AppNames
        {
            get; set;
        } = new List<string>();

        public TimeSpan RunInterval
        {
            get => ConfigurationSettings?.RunInterval ?? TimeSpan.MinValue;

            set
            {
                if (ConfigurationSettings != null)
                {
                    ConfigurationSettings.RunInterval = value;
                }
            }
        }

        public TimeSpan AsyncClusterOperationTimeoutSeconds
        {
            get; set;
        } = TimeSpan.FromSeconds(60);

        public int DataCapacity
        {
            get => ConfigurationSettings?.DataCapacity ?? 30;

            set
            {
                if (ConfigurationSettings != null)
                {
                    ConfigurationSettings.DataCapacity = value;
                }
            }
        }

        public bool UseCircularBuffer
        {
            get => ConfigurationSettings != null && ConfigurationSettings.UseCircularBuffer;

            set
            {
                if (ConfigurationSettings != null)
                {
                    ConfigurationSettings.UseCircularBuffer = value;
                }
            }
        }

        public TimeSpan MonitorDuration
        {
            get => ConfigurationSettings?.MonitorDuration ?? TimeSpan.MinValue;
            set
            {
                if (ConfigurationSettings != null)
                {
                    ConfigurationSettings.MonitorDuration = value;
                }
            }
        }

        protected bool IsTelemetryProviderEnabled
        {
            get; set;
        }

        protected ITelemetryProvider TelemetryClient
        {
            get; set;
        }

        protected bool IsEtwProviderEnabled
        {
            get; set;
        }

        protected FabricClient FabricClientInstance
        {
            get; set;
        }

        protected string ConfigurationSectionName
        {
            get;
        }

        public CsvFileWriteFormat CsvWriteFormat 
        {
            get; set; 
        }

        /// <summary>
        /// Base type constructor for all observers (both built-in and plugin impls).
        /// </summary>
        /// <param name="fabricClient">FO employs exactly one instance of a FabricClient object. This protects against memory abuse. FO will inject the instance when the application starts.</param>
        /// <param name="statelessServiceContext">The ServiceContext instance for FO, which is a Stateless singleton (1 partition) service that runs on all nodes in an SF cluster. FO will inject this instance when the application starts.</param>
        protected ObserverBase(FabricClient fabricClient, StatelessServiceContext statelessServiceContext)
        {
            ObserverName = GetType().Name;
            ConfigurationSectionName = ObserverName + "Configuration";
            FabricClientInstance = fabricClient;
            FabricServiceContext = statelessServiceContext;
            NodeName = FabricServiceContext.NodeContext.NodeName;
            NodeType = FabricServiceContext.NodeContext.NodeType;
            SetObserverConfiguration();

            // Observer Logger setup.
            string logFolderBasePath;
            string observerLogPath = GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.ObserverLogPathParameter);
            
            if (!string.IsNullOrEmpty(observerLogPath))
            {
                logFolderBasePath = observerLogPath;
            }
            else
            {
                string logFolderBase = Path.Combine(Environment.CurrentDirectory, "observer_logs");
                logFolderBasePath = logFolderBase;
            }

            ObserverLogger = new Logger(ObserverName, logFolderBasePath, MaxLogArchiveFileLifetimeDays)
            {
                EnableETWLogging = IsEtwProviderEnabled
            };

            if (string.IsNullOrEmpty(dumpsPath))
            {
                SetDefaultSfDumpPath();
            }

            ConfigurationSettings = new ConfigSettings(
                    FabricServiceContext.CodePackageActivationContext.GetConfigurationPackageObject("Config")?.Settings,
                    ConfigurationSectionName);

            ObserverLogger.EnableVerboseLogging = ConfigurationSettings.EnableVerboseLogging;
            HealthReporter = new ObserverHealthReporter(ObserverLogger, fabricClient);

            // This is so EnableVerboseLogging can (and should) be set to false and the ObserverWebApi service will still function.
            // If you renamed the application, then you must set the ObserverWebApiEnabled parameter to true in ObserverManagerConfiguration section 
            // in Settings.xml. Note the || in the conditional check below.
            IsObserverWebApiAppDeployed = 
                bool.TryParse(
                    GetSettingParameterValue(
                        ObserverConstants.ObserverManagerConfigurationSectionName,
                        ObserverConstants.ObserverWebApiEnabled), out bool obsWeb) && obsWeb && IsObserverWebApiAppInstalled();
        }

        /// <summary>
        /// This abstract method must be implemented by deriving type. This is called by ObserverManager on any observer instance that is enabled and ready to run.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A Task.</returns>
        public abstract Task ObserveAsync(CancellationToken token);

        /// <summary>
        /// This abstract method must be implemented by deriving type. This is called by an observer's ObserveAsync method when it is time to report on observations.
        /// It exists as its own abstract method to force a simple pattern in observer implementations: Initiate/orchestrate observations in one method. Do related health reporting in another.
        /// This is a simple design choice that is enforced.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A Task.</returns>
        public abstract Task ReportAsync(CancellationToken token);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="property"></param>
        /// <param name="description"></param>
        /// <param name="level"></param>
        public void WriteToLogWithLevel(string property, string description, LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Information:
                    ObserverLogger.LogInfo("{0} logged at level {1}: {2}", property, level, description);
                    break;

                case LogLevel.Warning:
                    ObserverLogger.LogWarning("{0} logged at level {1}: {2}", property, level, description);
                    break;

                case LogLevel.Error:
                    ObserverLogger.LogError("{0} logged at level {1}: {2}", property, level, description);
                    break;

                default:
                    return;
            }

            Logger.Flush();
        }

        /// <summary>
        /// Gets a parameter value from the specified section.
        /// </summary>
        /// <param name="sectionName">Name of the section.</param>
        /// <param name="parameterName">Name of the parameter.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <returns>parameter value.</returns>
        public string GetSettingParameterValue(string sectionName, string parameterName, string defaultValue = null)
        {
            if (string.IsNullOrEmpty(sectionName) || string.IsNullOrEmpty(parameterName))
            {
                return null;
            }

            try
            {
                ConfigurationPackage serviceConfiguration = FabricServiceContext.CodePackageActivationContext.GetConfigurationPackageObject("Config");

                if (serviceConfiguration == null)
                {
                    return null;
                }

                if (serviceConfiguration.Settings.Sections.All(sec => sec.Name != sectionName))
                {
                    return null;
                }

                if (serviceConfiguration.Settings.Sections[sectionName].Parameters.All(param => param.Name != parameterName))
                {
                    return null;
                }

                string setting = serviceConfiguration.Settings.Sections[sectionName].Parameters[parameterName]?.Value;

                if (string.IsNullOrEmpty(setting) && defaultValue != null)
                {
                    return defaultValue;
                }

                return setting;
            }
            catch (ArgumentException)
            {

            }

            return null;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        // Windows process dmp creator.
        /// <summary>
        /// This function will create Windows process dumps in supplied location if there is enough disk space available.
        /// This function runs if you set dumpProcessOnError to true in AppObserver.config.json, for example.
        /// </summary>
        /// <param name="processId">Process id of the target process to dump.</param>
        /// <param name="dumpType">Optional: The type of dump to generate. Default is DumpType.Full.</param>
        /// <param name="filePath">Optional: The full path to store dump file. Default is %SFLogRoot%\CrashDumps</param>
        /// <returns>true or false if the operation succeeded.</returns>
        public bool DumpServiceProcessWindows(int processId, DumpType dumpType = DumpType.Full, string filePath = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            if (string.IsNullOrEmpty(dumpsPath) && string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            string path = !string.IsNullOrEmpty(filePath) ? filePath : dumpsPath;
            string processName = string.Empty;

            NativeMethods.MINIDUMP_TYPE miniDumpType;

            switch (dumpType)
            {
                case DumpType.Full:
                    miniDumpType = NativeMethods.MINIDUMP_TYPE.MiniDumpWithFullMemory |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpWithFullMemoryInfo |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpWithHandleData |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpWithThreadInfo |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpWithUnloadedModules;
                    break;

                case DumpType.MiniPlus:
                    miniDumpType = NativeMethods.MINIDUMP_TYPE.MiniDumpWithPrivateReadWriteMemory |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpWithDataSegs |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpWithHandleData |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpWithFullMemoryInfo |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpWithThreadInfo |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpWithUnloadedModules;
                    break;

                case DumpType.Mini:
                    miniDumpType = NativeMethods.MINIDUMP_TYPE.MiniDumpWithIndirectlyReferencedMemory |
                                   NativeMethods.MINIDUMP_TYPE.MiniDumpScanMemory;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(dumpType), dumpType, null);
            }

            try
            {
                // This is to ensure friendly-name of resulting dmp file.
                using (Process process = Process.GetProcessById(processId))
                {
                    processName = process.ProcessName;

                    if (string.IsNullOrEmpty(processName))
                    {
                        return false;
                    }

                    processName += $"_{DateTime.Now:ddMMyyyyHHmmss}.dmp";
                    IntPtr processHandle = process.Handle;

                    // Check disk space availability before writing dump file.
                    string driveName = path.Substring(0, 2);
                    
                    if (DiskUsage.GetCurrentDiskSpaceUsedPercent(driveName) > 90)
                    {
                        HealthReporter.ReportFabricObserverServiceHealth(
                            FabricServiceContext.ServiceName.OriginalString,
                            ObserverName,
                            HealthState.Warning,
                            "Not enough disk space available for dump file creation.");

                        return false;
                    }

                    using (FileStream file = File.Create(Path.Combine(path, processName)))
                    {
                        if (!NativeMethods.MiniDumpWriteDump(
                                            processHandle,
                                            (uint)processId,
                                            file.SafeFileHandle,
                                            miniDumpType,
                                            IntPtr.Zero,
                                            IntPtr.Zero,
                                            IntPtr.Zero))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        }
                    }
                }

                return true;
            }
            catch (Exception e) when (
                    e is ArgumentException ||
                    e is InvalidOperationException ||
                    e is IOException ||
                    e is PlatformNotSupportedException ||
                    e is UnauthorizedAccessException ||
                    e is Win32Exception)
            {
                ObserverLogger.LogWarning(
                    $"Failure generating Windows process dump file {processName} with error:{Environment.NewLine}{e}");
            }

            return false;
        }

        /// <summary>
        /// This function *only* processes *numeric* data held in (FabricResourceUsageData (FRUD) instances and generates Application or Node level Health Reports depending on supplied Error and Warning thresholds. 
        /// </summary>
        /// <typeparam name="T">Generic: This represents the numeric type of data this function will operate on.</typeparam>
        /// <param name="data">FabricResourceUsageData (FRUD) instance.</param>
        /// <param name="thresholdError">Error threshold (numeric)</param>
        /// <param name="thresholdWarning">Warning threshold (numeric)</param>
        /// <param name="healthReportTtl">Health report Time to Live (TimeSpan)</param>
        /// <param name="healthReportType">HealthReport type. Note, only Application and Node health report types are supported by this function.</param>
        /// <param name="replicaOrInstance">Replica or Instance information contained in a type.</param>
        /// <param name="dumpOnError">Whether or not to dump process if Error threshold has been reached.</param>
        public void ProcessResourceDataReportHealth<T>(
                        FabricResourceUsageData<T> data,
                        T thresholdError,
                        T thresholdWarning,
                        TimeSpan healthReportTtl,
                        HealthReportType healthReportType = HealthReportType.Node,
                        ReplicaOrInstanceMonitoringInfo replicaOrInstance = null,
                        bool dumpOnError = false) where T : struct
        {
            if (data == null)
            {
                return;
            }

            if (healthReportType != HealthReportType.Application && healthReportType != HealthReportType.Node)
            {
                ObserverLogger.LogWarning($"ProcessResourceDataReportHealth: Unsupported HealthReport type -> {Enum.GetName(typeof(HealthReportType), healthReportType)}");
                return;
            }

            string thresholdName = "Minimum";
            bool warningOrError = false;
            string name = string.Empty, id, drive = string.Empty, procId = string.Empty;
            T threshold = thresholdWarning;
            HealthState healthState = HealthState.Ok;
            Uri appName = null;
            Uri serviceName = null;
            TelemetryData telemetryData;

            if (healthReportType == HealthReportType.Application)
            {
                if (replicaOrInstance != null)
                {
                    // Create a unique id which will be used for health Warnings and OKs (clears).
                    appName = replicaOrInstance.ApplicationName;
                    serviceName = replicaOrInstance.ServiceName;
                    name = serviceName.OriginalString.Replace($"{appName.OriginalString}/", string.Empty);
                    procId = replicaOrInstance.HostProcessId.ToString();
                }
                else // System service report from FabricSystemObserver.
                {
                    appName = new Uri(FabricSystemAppName);
                    name = data.Id;

                    try
                    {
                        procId = Process.GetProcessesByName(name).First()?.Id.ToString();
                    }
                    catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is PlatformNotSupportedException)
                    {

                    }
                }

                id = $"{NodeName}_{name}_{data.Property.Replace(" ", string.Empty)}";

                // The health event description will be a serialized instance of telemetryData,
                // so it should be completely constructed (filled with data) regardless
                // of user telemetry settings.
                telemetryData = new TelemetryData(FabricClientInstance, Token)
                {
                    ApplicationName = appName?.OriginalString ?? string.Empty,
                    NodeName = NodeName,
                    ObserverName = ObserverName,
                    Metric = data.Property,
                    Value = Math.Round(data.AverageDataValue, 0),
                    PartitionId = replicaOrInstance?.PartitionId.ToString(),
                    ProcessId = procId,
                    ReplicaId = replicaOrInstance?.ReplicaOrInstanceId.ToString(),
                    ServiceName = serviceName?.OriginalString ?? string.Empty,
                    SystemServiceProcessName = appName?.OriginalString == FabricSystemAppName ? name : string.Empty,
                    Source = ObserverConstants.FabricObserverName
                };

                // If the source issue is from FSO, then set the SystemServiceProcessName on TD instance.
                if (appName != null && appName.OriginalString == FabricSystemAppName)
                {
                    telemetryData.SystemServiceProcessName = name;
                }

                // Container
                if (!string.IsNullOrEmpty(replicaOrInstance?.ContainerId))
                {
                    telemetryData.ContainerId = replicaOrInstance.ContainerId;
                }

                // Telemetry - This is informational, per reading telemetry, healthstate is irrelevant here.
                // Enable this for your observer if you want to send data to ApplicationInsights or LogAnalytics for each resource usage observation it makes per specified metric.
                if (IsTelemetryEnabled)
                {
                    _ = TelemetryClient?.ReportMetricAsync(telemetryData, Token).ConfigureAwait(false);
                }

                // ETW - This is informational, per reading EventSource tracing, healthstate is irrelevant here.
                // Enable this for your observer if you want to log etw (which can then be read by some agent that will send it to some endpoint)
                // for each resource usage observation it makes per specified metric.
                if (IsEtwEnabled)
                {
                    ObserverLogger.LogEtw(
                                    ObserverConstants.FabricObserverETWEventName,
                                    new
                                    {
                                        ApplicationName = appName?.OriginalString ?? string.Empty,
                                        NodeName,
                                        ObserverName,
                                        Metric = data.Property,
                                        Value = Math.Round(data.AverageDataValue, 0),
                                        PartitionId = replicaOrInstance?.PartitionId.ToString(),
                                        ProcessId = procId,
                                        ReplicaId = replicaOrInstance?.ReplicaOrInstanceId.ToString(),
                                        ServiceName = serviceName?.OriginalString ?? string.Empty,
                                        Source = ObserverConstants.FabricObserverName,
                                        SystemServiceProcessName = appName?.OriginalString == FabricSystemAppName ? name : string.Empty
                                    });
                }
            }
            else
            {
                drive = string.Empty;
                id = data.Id;

                if (ObserverName == ObserverConstants.DiskObserverName)
                {
                    drive = $"{id}: ";

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        drive = $"{id.Remove(1, 2)}: ";
                    }
                }

                // The health event description will be a serialized instance of telemetryData,
                // so it should be completely constructed (filled with data) regardless
                // of user telemetry settings.
                telemetryData = new TelemetryData(FabricClientInstance, Token)
                {
                    NodeName = NodeName,
                    ObserverName = ObserverName,
                    Metric = $"{drive}{data.Property}",
                    Source = ObserverConstants.FabricObserverName,
                    Value = Math.Round(data.AverageDataValue, 0)
                };

                if (IsTelemetryEnabled)
                {
                    _ = TelemetryClient?.ReportMetricAsync(telemetryData, Token).ConfigureAwait(false);
                }

                if (IsEtwEnabled)
                {
                    ObserverLogger.LogEtw(
                                    ObserverConstants.FabricObserverETWEventName,
                                    new
                                    {
                                        NodeName,
                                        ObserverName,
                                        Metric = $"{drive}{data.Property}",
                                        Source = ObserverConstants.FabricObserverName,
                                        Value = Math.Round(data.AverageDataValue, 0)
                                    });
                }
            }

            // Health Error
            if (data.IsUnhealthy(thresholdError))
            {
                thresholdName = "Maximum";
                threshold = thresholdError;
                warningOrError = true;
                healthState = HealthState.Error;

                // **Windows-only**. This is primarily useful for AppObserver, but makes sense to be
                // part of the base class for future use, like for FSO.
                if (replicaOrInstance != null && dumpOnError && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        int pid = (int)replicaOrInstance.HostProcessId;

                        using (var proc = Process.GetProcessById(pid))
                        {
                            string procName = proc?.ProcessName;

                            if (!serviceDumpCountDictionary.ContainsKey(procName))
                            {
                                serviceDumpCountDictionary.Add(procName, 0);
                            }

                            if (serviceDumpCountDictionary[procName] < MaxDumps)
                            {
                                // DumpServiceProcess defaults to a Full dump with
                                // process memory, handles and thread data.
                                bool success = DumpServiceProcessWindows(pid);

                                if (success)
                                {
                                    serviceDumpCountDictionary[procName]++;
                                }
                            }
                        }
                    }

                    // Ignore these, it just means no dmp will be created.This is not
                    // critical to FO. Log as info, not warning.
                    catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is Win32Exception)
                    {
                        ObserverLogger.LogInfo($"Unable to generate dmp file:{Environment.NewLine}{e}");
                    }
                }
            }

            // Health Warning
            if (!warningOrError && data.IsUnhealthy(thresholdWarning))
            {
                warningOrError = true;
                healthState = HealthState.Warning;
            }

            if (warningOrError)
            {
                string errorWarningCode = null;

                switch (data.Property)
                {
                    case ErrorWarningProperty.TotalCpuTime when healthReportType == HealthReportType.Application:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.AppErrorCpuPercent : FOErrorWarningCodes.AppWarningCpuPercent;
                        break;

                    case ErrorWarningProperty.TotalCpuTime:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorCpuPercent : FOErrorWarningCodes.NodeWarningCpuPercent;
                        break;

                    case ErrorWarningProperty.DiskSpaceUsagePercentage:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorDiskSpacePercent : FOErrorWarningCodes.NodeWarningDiskSpacePercent;
                        break;

                    case ErrorWarningProperty.DiskSpaceUsageMb:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorDiskSpaceMB : FOErrorWarningCodes.NodeWarningDiskSpaceMB;
                        break;

                    case ErrorWarningProperty.TotalMemoryConsumptionMb when healthReportType == HealthReportType.Application:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.AppErrorMemoryMB : FOErrorWarningCodes.AppWarningMemoryMB;
                        break;

                    case ErrorWarningProperty.TotalMemoryConsumptionMb:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorMemoryMB : FOErrorWarningCodes.NodeWarningMemoryMB;
                        break;

                    case ErrorWarningProperty.TotalMemoryConsumptionPct when replicaOrInstance != null:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.AppErrorMemoryPercent : FOErrorWarningCodes.AppWarningMemoryPercent;
                        break;

                    case ErrorWarningProperty.TotalMemoryConsumptionPct:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorMemoryPercent : FOErrorWarningCodes.NodeWarningMemoryPercent;
                        break;

                    case ErrorWarningProperty.DiskAverageQueueLength:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorDiskAverageQueueLength : FOErrorWarningCodes.NodeWarningDiskAverageQueueLength;
                        break;

                    case ErrorWarningProperty.TotalActiveFirewallRules:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.ErrorTooManyFirewallRules : FOErrorWarningCodes.WarningTooManyFirewallRules;
                        break;

                    case ErrorWarningProperty.TotalActivePorts when healthReportType == HealthReportType.Application:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.AppErrorTooManyActiveTcpPorts : FOErrorWarningCodes.AppWarningTooManyActiveTcpPorts;
                        break;

                    case ErrorWarningProperty.TotalActivePorts:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorTooManyActiveTcpPorts : FOErrorWarningCodes.NodeWarningTooManyActiveTcpPorts;
                        break;

                    case ErrorWarningProperty.TotalEphemeralPorts when healthReportType == HealthReportType.Application:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.AppErrorTooManyActiveEphemeralPorts : FOErrorWarningCodes.AppWarningTooManyActiveEphemeralPorts;
                        break;

                    case ErrorWarningProperty.TotalEphemeralPorts:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorTooManyActiveEphemeralPorts : FOErrorWarningCodes.NodeWarningTooManyActiveEphemeralPorts;
                        break;

                    case ErrorWarningProperty.TotalFileHandles when healthReportType == HealthReportType.Application:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.AppErrorTooManyOpenFileHandles : FOErrorWarningCodes.AppWarningTooManyOpenFileHandles;
                        break;

                    case ErrorWarningProperty.TotalFileHandles:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorTooManyOpenFileHandles : FOErrorWarningCodes.NodeWarningTooManyOpenFileHandles;
                        break;

                    case ErrorWarningProperty.TotalFileHandlesPct:
                        errorWarningCode = (healthState == HealthState.Error) ?
                            FOErrorWarningCodes.NodeErrorTotalOpenFileHandlesPercent : FOErrorWarningCodes.NodeWarningTotalOpenFileHandlesPercent;
                        break;
                }

                var healthMessage = new StringBuilder();

                _ = healthMessage.Append($"{drive}{data.Property} is at or above the specified {thresholdName} limit ({threshold}{data.Units})");
                _ = healthMessage.Append($" - {data.Property}: {Math.Round(data.AverageDataValue, 0)}{data.Units}");

                // The health event description will be a serialized instance of telemetryData,
                // so it should be completely constructed (filled with data) regardless
                // of user telemetry settings.
                telemetryData.ApplicationName = appName?.OriginalString ?? string.Empty;
                telemetryData.Code = errorWarningCode;

                if (replicaOrInstance != null && !string.IsNullOrEmpty(replicaOrInstance.ContainerId))
                {
                    telemetryData.ContainerId = replicaOrInstance.ContainerId;
                }

                telemetryData.HealthState = Enum.GetName(typeof(HealthState), healthState);
                telemetryData.Description = healthMessage.ToString();

                // Send Health Report as Telemetry event (perhaps it signals an Alert from App Insights, for example.).
                if (IsTelemetryEnabled)
                {
                    _ = TelemetryClient?.ReportHealthAsync(telemetryData, Token).ConfigureAwait(false);
                }

                // ETW.
                if (IsEtwEnabled)
                {
                    ObserverLogger.LogEtw(
                                    ObserverConstants.FabricObserverETWEventName,
                                    new
                                    {
                                        ApplicationName = appName?.OriginalString ?? string.Empty,
                                        Code = errorWarningCode,
                                        ContainerId = replicaOrInstance != null ? replicaOrInstance.ContainerId ?? string.Empty : string.Empty,
                                        HealthState = Enum.GetName(typeof(HealthState), healthState),
                                        Description = healthMessage.ToString(),
                                        Metric = $"{drive}{data.Property}",
                                        Node = NodeName,
                                        ProcessId = procId,
                                        ServiceName = serviceName?.OriginalString ?? string.Empty,
                                        Source = ObserverConstants.FabricObserverName,
                                        SystemServiceProcessName = appName?.OriginalString == FabricSystemAppName ? name : string.Empty,
                                        Value = Math.Round(data.AverageDataValue, 0)
                                    });
                }

                var healthReport = new HealthReport
                {
                    AppName = appName,
                    Code = errorWarningCode,
                    EmitLogEvent = EnableVerboseLogging || IsObserverWebApiAppDeployed,
                    HealthData = telemetryData,
                    HealthMessage = healthMessage.ToString(),
                    HealthReportTimeToLive = healthReportTtl,
                    ReportType = healthReportType,
                    State = healthState,
                    NodeName = NodeName,
                    Observer = ObserverName,
                    Property = id,
                    ResourceUsageDataProperty = data.Property,
                    SourceId = $"{ObserverName}({errorWarningCode})"
                };

                if (AppNames.All(a => a != appName?.OriginalString))
                {
                    AppNames.Add(appName?.OriginalString);
                }

                // Generate a Service Fabric Health Report.
                HealthReporter.ReportHealthToServiceFabric(healthReport);

                // Set internal health state info on data instance.
                data.ActiveErrorOrWarning = true;
                data.ActiveErrorOrWarningCode = errorWarningCode;

                // This means this observer created a Warning or Error SF Health Report
                HasActiveFabricErrorOrWarning = true;

                // Clean up sb.
                _ = healthMessage.Clear();
            }
            else
            {
                if (data.ActiveErrorOrWarning)
                {
                    // The health event description will be a serialized instance of telemetryData,
                    // so it should be completely constructed (filled with data) regardless
                    // of user telemetry settings.
                    telemetryData.ApplicationName = appName?.OriginalString ?? string.Empty;
                    telemetryData.Code = FOErrorWarningCodes.Ok;

                    if (replicaOrInstance != null && !string.IsNullOrEmpty(replicaOrInstance.ContainerId))
                    {
                        telemetryData.ContainerId = replicaOrInstance.ContainerId;
                    }

                    telemetryData.HealthState = Enum.GetName(typeof(HealthState), HealthState.Ok);
                    telemetryData.Description = $"{data.Property} is now within normal/expected range.";
                    telemetryData.Metric = data.Property;
                    telemetryData.Source = ObserverConstants.FabricObserverName;
                    telemetryData.Value = Math.Round(data.AverageDataValue, 0);

                    // Telemetry
                    if (IsTelemetryEnabled)
                    {
                        _ = TelemetryClient?.ReportHealthAsync(telemetryData, Token);
                    }

                    // ETW.
                    if (IsEtwEnabled)
                    {
                        ObserverLogger.LogEtw(
                                        ObserverConstants.FabricObserverETWEventName,
                                        new
                                        {
                                            ApplicationName = appName != null ? appName.OriginalString : string.Empty,
                                            Code = data.ActiveErrorOrWarningCode,
                                            ContainerId = replicaOrInstance != null ? replicaOrInstance.ContainerId ?? string.Empty : string.Empty,
                                            HealthState = Enum.GetName(typeof(HealthState), HealthState.Ok),
                                            Description = $"{data.Property} is now within normal/expected range.",
                                            Metric = data.Property,
                                            Node = NodeName,
                                            ProcessId = replicaOrInstance?.HostProcessId.ToString(),
                                            ServiceName = name ?? string.Empty,
                                            Source = ObserverConstants.FabricObserverName,
                                            SystemServiceProcessName = appName?.OriginalString == FabricSystemAppName ? name : string.Empty,
                                            Value = Math.Round(data.AverageDataValue, 0)
                                        });
                    }

                    var healthReport = new HealthReport
                    {
                        AppName = appName,
                        Code = data.ActiveErrorOrWarningCode,
                        EmitLogEvent = EnableVerboseLogging || IsObserverWebApiAppDeployed,
                        HealthData = telemetryData,
                        HealthMessage = $"{data.Property} is now within normal/expected range.",
                        HealthReportTimeToLive = default,
                        ReportType = healthReportType,
                        State = HealthState.Ok,
                        NodeName = NodeName,
                        Observer = ObserverName,
                        Property = id,
                        ResourceUsageDataProperty = data.Property,
                        SourceId = $"{ObserverName}({data.ActiveErrorOrWarningCode})"
                    };

                    // Emit an Ok Health Report to clear Fabric Health warning.
                    HealthReporter.ReportHealthToServiceFabric(healthReport);

                    // Reset health states.
                    data.ActiveErrorOrWarning = false;
                    data.ActiveErrorOrWarningCode = FOErrorWarningCodes.Ok;
                    HasActiveFabricErrorOrWarning = false;
                }
            }

            // No need to keep data in memory.
            if (data.Data is List<T> list)
            {
                // List<T> impl.
                list.Clear();
                list.TrimExcess();
            }
            else
            {
                // CircularBufferCollection<T> impl.
                data.Data.Clear();
            }
        }

        /// <summary>
        /// Computes TTL for an observer's health reports based on how long it takes an observer to run, time differential between runs,
        /// observer loop sleep time, plus a little more time to guarantee that a health report will remain active until the next time the observer runs.
        /// Note that if you set a RunInterval on an observer, that will be reflected here and the Warning will last for that amount of time at least.
        /// </summary>
        /// <returns>TimeSpan that contains the TTL value.</returns>
        public TimeSpan GetHealthReportTimeToLive()
        {
            _ = int.TryParse(
                    GetSettingParameterValue(
                        ObserverConstants.ObserverManagerConfigurationSectionName,
                        ObserverConstants.ObserverLoopSleepTimeSeconds),
                    out int obsSleepTime);

            // First run.
            if (LastRunDateTime == DateTime.MinValue)
            {
                return TimeSpan.FromSeconds(obsSleepTime)
                        .Add(TimeSpan.FromMinutes(TtlAddMinutes))
                        .Add(RunInterval > TimeSpan.MinValue ? RunInterval : TimeSpan.Zero);
            }

            return DateTime.Now.Subtract(LastRunDateTime)
                    .Add(TimeSpan.FromSeconds(RunDuration > TimeSpan.MinValue ? RunDuration.TotalSeconds : 0))
                    .Add(TimeSpan.FromSeconds(obsSleepTime));
        }

        // This is here so each Observer doesn't have to implement IDisposable.
        // Just override in observer impls that do implement IDisposable.
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                disposed = true;
            }
        }

        private void SetDefaultSfDumpPath()
        {
            // This only needs to be set once.
            if (string.IsNullOrEmpty(dumpsPath))
            {
                SFLogRoot = ServiceFabricConfiguration.Instance.FabricLogRoot;

                if (!string.IsNullOrEmpty(SFLogRoot))
                {
                    dumpsPath = Path.Combine(SFLogRoot, "CrashDumps");
                }
            }

            if (Directory.Exists(dumpsPath))
            {
                return;
            }

            try
            {
                _ = Directory.CreateDirectory(dumpsPath);
            }
            catch (IOException e)
            {
                HealthReporter.ReportFabricObserverServiceHealth(
                                FabricServiceContext.ServiceName.ToString(),
                                ObserverName,
                                HealthState.Warning,
                                $"Unable to create dumps directory:{Environment.NewLine}{e}");

                dumpsPath = null;
            }
        }

        private void SetObserverConfiguration()
        {
            // Archive file lifetime - ObserverLogger files.
            if (int.TryParse(GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.MaxArchivedLogFileLifetimeDays), out int maxFileArchiveLifetime))
            {
                MaxLogArchiveFileLifetimeDays = maxFileArchiveLifetime;
            }

            // ETW
            if (bool.TryParse(GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.EnableETWProvider), out bool etwProviderEnabled))
            {
                IsEtwProviderEnabled = etwProviderEnabled;
            }

            // Telemetry.
            if (bool.TryParse(GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.TelemetryEnabled), out bool telemEnabled))
            {
                IsTelemetryProviderEnabled = telemEnabled;
            }

            if (!IsTelemetryProviderEnabled)
            {
                return;
            }

            string telemetryProviderType = GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.TelemetryProviderType);

            if (string.IsNullOrEmpty(telemetryProviderType))
            {
                IsTelemetryProviderEnabled = false;

                return;
            }

            if (!Enum.TryParse(telemetryProviderType, out TelemetryProviderType telemetryProvider))
            {
                IsTelemetryProviderEnabled = false;

                return;
            }

            switch (telemetryProvider)
            {
                case TelemetryProviderType.AzureLogAnalytics:
                        
                    string logAnalyticsLogType =
                        GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.LogAnalyticsLogTypeParameter);

                    string logAnalyticsSharedKey =
                        GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.LogAnalyticsSharedKeyParameter);

                    string logAnalyticsWorkspaceId =
                        GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.LogAnalyticsWorkspaceIdParameter);

                    if (string.IsNullOrEmpty(logAnalyticsWorkspaceId) || string.IsNullOrEmpty(logAnalyticsSharedKey))
                    {
                        IsTelemetryProviderEnabled = false;
                        return;
                    }

                    TelemetryClient = new LogAnalyticsTelemetry(
                        logAnalyticsWorkspaceId,
                        logAnalyticsSharedKey,
                        logAnalyticsLogType,
                        FabricClientInstance,
                        new CancellationToken());

                    break;
                        

                case TelemetryProviderType.AzureApplicationInsights:
                        
                    string aiKey = GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.AiKey);

                    if (string.IsNullOrEmpty(aiKey))
                    {
                        IsTelemetryProviderEnabled = false;
                        return;
                    }

                    TelemetryClient = new AppInsightsTelemetry(aiKey);
                    break;
                        

                default:

                    IsTelemetryProviderEnabled = false;
                    break;
            }
        }

        private void InitializeCsvLogger()
        {
            if (CsvFileLogger != null)
            {
                return;
            }

            // Archive file lifetime - CsvLogger files.
            if (int.TryParse(GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.MaxArchivedCsvFileLifetimeDays), out int maxCsvFileArchiveLifetime))
            {
                MaxCsvArchiveFileLifetimeDays = maxCsvFileArchiveLifetime;
            }

            // Csv file write format - CsvLogger only.
            if (Enum.TryParse(GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.CsvFileWriteFormat), ignoreCase: true, out CsvFileWriteFormat csvWriteFormat))
            {
                CsvWriteFormat = csvWriteFormat;
            }

            CsvFileLogger = new DataTableFileLogger
            {
                FileWriteFormat = CsvWriteFormat,
                MaxArchiveCsvFileLifetimeDays = MaxCsvArchiveFileLifetimeDays
            };

            string dataLogPath = GetSettingParameterValue(ObserverConstants.ObserverManagerConfigurationSectionName, ObserverConstants.DataLogPathParameter);

            CsvFileLogger.BaseDataLogFolderPath = !string.IsNullOrEmpty(dataLogPath) ? Path.Combine(dataLogPath, ObserverName) : Path.Combine(Environment.CurrentDirectory, "fabric_observer_csvdata", ObserverName);
        }

        private bool IsObserverWebApiAppInstalled()
        {
            try
            {
                var deployedObsWebApps = FabricClientInstance.QueryManager.GetApplicationListAsync(new Uri("fabric:/FabricObserverWebApi")).GetAwaiter().GetResult();
                return deployedObsWebApps?.Count > 0;
            }
            catch (Exception e) when (e is FabricException || e is TimeoutException)
            {

            }

            return false;
        }
    }
}