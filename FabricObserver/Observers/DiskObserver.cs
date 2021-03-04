﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Fabric;
using System.Fabric.Health;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FabricObserver.Observers.Utilities;

namespace FabricObserver.Observers
{
    // This observer monitors logical disk behavior and signals Service Fabric Warning or Error events based on user-supplied thresholds
    // in Settings.xml.
    // The output (a local file) is used by the API service and the HTML frontend (http://localhost:5000/api/ObserverManager).
    // Health Report processor will also emit ETW telemetry if configured in Settings.xml.
    public class DiskObserver : ObserverBase
    {
        // Data storage containers for post run analysis.
        private readonly List<FabricResourceUsageData<float>> DiskAverageQueueLengthData;
        private readonly List<FabricResourceUsageData<double>> DiskSpaceUsagePercentageData;
        private readonly List<FabricResourceUsageData<double>> DiskSpaceAvailableMbData;
        private readonly List<FabricResourceUsageData<double>> DiskSpaceTotalMbData;
        private readonly Stopwatch stopWatch;
        private StringBuilder diskInfo = new StringBuilder();

        public int DiskSpacePercentErrorThreshold
        {
            get; set;
        }

        public int DiskSpacePercentWarningThreshold
        {
            get; set;
        }

        public int AverageQueueLengthWarningThreshold
        {
            get; set;
        }

        public int AverageQueueLengthErrorThreshold
        {
            get; set;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DiskObserver"/> class.
        /// </summary>
        public DiskObserver(FabricClient fabricClient, StatelessServiceContext context)
            : base(fabricClient, context)
        {
            DiskSpaceUsagePercentageData = new List<FabricResourceUsageData<double>>();
            DiskSpaceAvailableMbData = new List<FabricResourceUsageData<double>>();
            DiskSpaceTotalMbData = new List<FabricResourceUsageData<double>>();
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                DiskAverageQueueLengthData = new List<FabricResourceUsageData<float>>();
            }

            stopWatch = new Stopwatch();
        }

        public override async Task ObserveAsync(CancellationToken token)
        {
            // If set, this observer will only run during the supplied interval.
            // See Settings.xml, CertificateObserverConfiguration section, RunInterval parameter for an example.
            if (RunInterval > TimeSpan.MinValue
                && DateTime.Now.Subtract(LastRunDateTime) < RunInterval)
            {
                return;
            }

            SetErrorWarningThresholds();

            DriveInfo[] allDrives = DriveInfo.GetDrives();

            if (ObserverManager.ObserverWebAppDeployed)
            {
                diskInfo = new StringBuilder();
            }

            foreach (var d in allDrives)
            {
                token.ThrowIfCancellationRequested();

                if (!DiskUsage.ShouldCheckDrive(d))
                {
                    continue;
                }

                // This section only needs to run if you have the FabricObserverWebApi app installed.
                if (ObserverManager.ObserverWebAppDeployed)
                {
                    _ = diskInfo.AppendFormat("\n\nDrive Name: {0}\n", d.Name);

                    // Logging.
                    _ = diskInfo.AppendFormat("Drive Type: {0}\n", d.DriveType);
                    _ = diskInfo.AppendFormat("  Volume Label   : {0}\n", d.VolumeLabel);
                    _ = diskInfo.AppendFormat("  Filesystem     : {0}\n", d.DriveFormat);
                    _ = diskInfo.AppendFormat("  Total Disk Size: {0} GB\n", d.TotalSize / 1024 / 1024 / 1024);
                    _ = diskInfo.AppendFormat("  Root Directory : {0}\n", d.RootDirectory);
                    _ = diskInfo.AppendFormat("  Free User : {0} GB\n", d.AvailableFreeSpace / 1024 / 1024 / 1024);
                    _ = diskInfo.AppendFormat("  Free Total: {0} GB\n", d.TotalFreeSpace / 1024 / 1024 / 1024);
                    _ = diskInfo.AppendFormat("  % Used    : {0}%\n", DiskUsage.GetCurrentDiskSpaceUsedPercent(d.Name));
                }

                // Setup monitoring data structures.
                string id = d.Name;

                // Since these live across iterations, do not duplicate them in the containing list.
                // Disk space %.
                if (DiskSpaceUsagePercentageData.All(data => data.Id != id) && (DiskSpacePercentErrorThreshold > 0 || DiskSpacePercentWarningThreshold > 0))
                {
                    DiskSpaceUsagePercentageData.Add(new FabricResourceUsageData<double>(ErrorWarningProperty.DiskSpaceUsagePercentage, id, DataCapacity));
                }

                // Current disk queue length. Windows only.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && DiskAverageQueueLengthData.All(data => data.Id != id) && (AverageQueueLengthErrorThreshold > 0 || AverageQueueLengthWarningThreshold > 0))
                {
                    DiskAverageQueueLengthData.Add(new FabricResourceUsageData<float>(ErrorWarningProperty.DiskAverageQueueLength, id, DataCapacity));
                }

                // This data is just used for Telemetry today.
                if (DiskSpaceAvailableMbData.All(data => data.Id != id))
                {
                    DiskSpaceAvailableMbData.Add(new FabricResourceUsageData<double>(ErrorWarningProperty.DiskSpaceAvailableMb, id, DataCapacity));
                }

                // This data is just used for Telemetry today.
                if (DiskSpaceTotalMbData.All(data => data.Id != id))
                {
                    DiskSpaceTotalMbData.Add(new FabricResourceUsageData<double>(ErrorWarningProperty.DiskSpaceTotalMb, id, DataCapacity));
                }

                // It is important to check if code is running on Windows, since d.Name.Substring(0, 2) will fail on Linux for / (root) mount point.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (AverageQueueLengthErrorThreshold > 0 || AverageQueueLengthWarningThreshold > 0)
                    {
                        // Warm up counter.
                        _ = DiskUsage.GetAverageDiskQueueLength(d.Name.Substring(0, 2));

                        DiskAverageQueueLengthData.Single(x => x.Id == id).Data.Add(DiskUsage.GetAverageDiskQueueLength(d.Name.Substring(0, 2)));
                    }
                }

                if (DiskSpacePercentErrorThreshold > 0 || DiskSpacePercentWarningThreshold > 0)
                {
                    DiskSpaceUsagePercentageData.Single(x => x.Id == id).Data.Add(DiskUsage.GetCurrentDiskSpaceUsedPercent(id));
                }

                DiskSpaceAvailableMbData.Single(x => x.Id == id).Data.Add(DiskUsage.GetAvailableDiskSpace(id, SizeUnit.Megabytes));
                DiskSpaceTotalMbData.Single(x => x.Id == id).Data.Add(DiskUsage.GetTotalDiskSpace(id, SizeUnit.Megabytes));

                // This section only needs to run if you have the FabricObserverWebApi app installed.
                if (ObserverManager.ObserverWebAppDeployed && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _ = diskInfo.AppendFormat(
                        "{0}",
                        GetWindowsPerfCounterDetailsText(
                            DiskAverageQueueLengthData.FirstOrDefault(
                                x => x.Id == d.Name.Substring(0, 1))
                                ?.Data,
                            "Avg. Disk Queue Length"));
                }

                RunDuration = stopWatch.Elapsed;
            }

            await ReportAsync(token).ConfigureAwait(true);
            LastRunDateTime = DateTime.Now;
        }

        public override Task ReportAsync(CancellationToken token)
        {
            try
            {
                var timeToLiveWarning = SetHealthReportTimeToLive();

                // User-supplied Disk Space Usage % thresholds from Settings.xml.
                foreach (var data in DiskSpaceUsagePercentageData)
                {
                    token.ThrowIfCancellationRequested();
                    ProcessResourceDataReportHealth(
                        data,
                        DiskSpacePercentErrorThreshold,
                        DiskSpacePercentWarningThreshold,
                        timeToLiveWarning);
                }

                // User-supplied Average disk queue length thresholds from Settings.xml. Windows only.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    foreach (var data in DiskAverageQueueLengthData)
                    {
                        token.ThrowIfCancellationRequested();
                        ProcessResourceDataReportHealth(
                            data,
                            AverageQueueLengthErrorThreshold,
                            AverageQueueLengthWarningThreshold,
                            timeToLiveWarning);
                    }
                }

                /* For ETW Only - These calls will just produce ETW (note the thresholds). */
                if (IsEtwProviderEnabled && IsObserverEtwEnabled)
                {
                    // Disk Space Available
                    foreach (var data in DiskSpaceAvailableMbData)
                    {
                        token.ThrowIfCancellationRequested();
                        ProcessResourceDataReportHealth(
                            data,
                            0,
                            0,
                            timeToLiveWarning);
                    }

                    // Disk Space Total
                    foreach (var data in DiskSpaceTotalMbData)
                    {
                        token.ThrowIfCancellationRequested();
                        ProcessResourceDataReportHealth(
                            data,
                            0,
                            0,
                            timeToLiveWarning);
                    }
                }

                token.ThrowIfCancellationRequested();

                // This section only needs to run if you have the FabricObserverWebApi app installed.
                if (!ObserverManager.ObserverWebAppDeployed)
                {
                    return Task.CompletedTask;
                }

                var diskInfoPath = Path.Combine(ObserverLogger.LogFolderBasePath, "disks.txt");

                _ = ObserverLogger.TryWriteLogFile(diskInfoPath, diskInfo.ToString());

                _ = diskInfo.Clear();

                return Task.CompletedTask;
            }
            catch (AggregateException e) when (e.InnerException is OperationCanceledException || e.InnerException is TaskCanceledException || e.InnerException is TimeoutException)
            {
                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                HealthReporter.ReportFabricObserverServiceHealth(
                        FabricServiceContext.ServiceName.OriginalString,
                        ObserverName,
                        HealthState.Warning,
                        $"Unhandled exception in GetSystemCpuMemoryValuesAsync:{Environment.NewLine}{e}");

                throw;
            }
        }

        private void SetErrorWarningThresholds()
        {
            try
            {
                if (int.TryParse(
                    GetSettingParameterValue(
                    ConfigurationSectionName,
                    ObserverConstants.DiskObserverDiskSpacePercentError), out int diskUsedError))
                {
                    DiskSpacePercentErrorThreshold = diskUsedError;
                }

                if (int.TryParse(
                    GetSettingParameterValue(
                    ConfigurationSectionName,
                    ObserverConstants.DiskObserverDiskSpacePercentWarning), out int diskUsedWarning))
                {
                    DiskSpacePercentWarningThreshold = diskUsedWarning;
                }

                if (int.TryParse(
                    GetSettingParameterValue(
                    ConfigurationSectionName,
                    ObserverConstants.DiskObserverAverageQueueLengthError), out int diskCurrentQueueLengthError))
                {
                    AverageQueueLengthErrorThreshold = diskCurrentQueueLengthError;
                }

                if (int.TryParse(
                    GetSettingParameterValue(
                    ConfigurationSectionName,
                    ObserverConstants.DiskObserverAverageQueueLengthWarning), out int diskCurrentQueueLengthWarning))
                {
                    AverageQueueLengthWarningThreshold = diskCurrentQueueLengthWarning;
                }
            }
            catch (ArgumentNullException)
            {
            }
            catch (FormatException)
            {
            }
        }

        private string GetWindowsPerfCounterDetailsText(
            ICollection<float> data,
            string counter)
        {
            if (data == null || data.Count == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            string ret;
            string unit = "ms";

            if (counter.Contains("Queue"))
            {
                unit = string.Empty;
            }

            _ = sb.AppendFormat("  {0}: {1}", counter, Math.Round(data.Average(), 3) + $" {unit}" + Environment.NewLine);

            ret = sb.ToString();
            _ = sb.Clear();

            return ret;
        }
    }
}