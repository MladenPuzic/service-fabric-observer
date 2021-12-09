﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace FabricObserver.Observers.Utilities
{
    public sealed class ErrorWarningProperty
    {
        // CPU/Memory
        public const string TotalCpuTime = "Total CPU Time";
        public const string TotalMemoryConsumptionMb = "Memory Consumption MB";
        public const string TotalMemoryConsumptionPct = "Memory Consumption %";

        // Certificates
        public const string CertificateExpiration = "Certificate Expiration";

        // Disk.
        public const string DiskAverageQueueLength = "Average Disk Queue Length";
        public const string DiskSpaceUsagePercentage = "Disk Space Consumption %";
        public const string DiskSpaceUsageMb = "Disk Space Consumption MB";
        public const string DiskSpaceAvailableMb = "Disk Space Available MB";
        public const string DiskSpaceTotalMb = "Disk Space Total MB";

        // Network
        public const string InternetConnectionFailure = "Outbound Internet Connection Failure";
        public const string TotalActiveFirewallRules = "Total Active Firewall Rules";
        public const string TotalActivePorts = "Total Active TCP Ports";
        public const string TotalEphemeralPorts = "Total Ephemeral Active TCP Ports";

        // File Handles
        public const string TotalFileHandles = "Total Allocated File Handles";
        public const string TotalFileHandlesPct = "Total Allocated File Handles %";

        // Threads
        public const string TotalThreadCount = "Total Thread Count";

        // Child procs
        public const string ChildProcessCount = "Child Process Count";

        // KVS LVIDs
        public const string TotalKvsLvidsPercent = "Total LVID Consumption %";
    }
}
