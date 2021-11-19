﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterObserver.Utilities
{
    public static class ObserverConstants
    {
        public const string ObserverManagerName = "ClusterObserverManager";
        public const string ObserverManagerConfigurationSectionName = "ObserverManagerConfiguration";
        public const string EnableVerboseLoggingParameter = "EnableVerboseLogging";
        public const string ObserverLogPath = "ObserverLogPath";
        public const string ObserverRunIntervalParameterName = "RunInterval";
        public const string ObserverEnabled = "Enabled";
        public const string AiKey = "AppInsightsInstrumentationKey";
        public const string EnableTelemetry = "EnableTelemetry";
        public const string AsyncOperationTimeoutSeconds = "AsyncOperationTimeoutSeconds";
        public const string ClusterObserverETWEventName = "ClusterObserverDataEvent";
        public const string EventSourceProviderName = "ClusterObserverETWProvider";
        public const string FabricObserver = "FabricObserver";

        // The name of the package that contains this Observer's configuration.
        public const string ObserverConfigurationPackageName = "Config";

        // Setting name for Runtime frequency of the Observer
        public const string ObserverLoopSleepTimeSeconds = "ObserverLoopSleepTimeSeconds";

        // Default to 1 minute if frequency is not supplied in config.
        public const int ObserverRunLoopSleepTimeSeconds = 60;

        // Setting name for Grace period of shutdown in seconds.
        public const string ObserverShutdownGracePeriodInSeconds = "ObserverShutdownGracePeriodInSeconds";

        // Setting name for Maximum time an observer should run before being considered hung or in some failure state.
        public const string ObserverExecutionTimeout = "ObserverExecutionTimeout";

        // EmitHealthWarningEvaluationDetails.
        public const string EmitHealthWarningEvaluationConfigurationSetting = "EmitHealthWarningEvaluationDetails";

        // Emit Repair Job information
        public const string MonitorRepairJobsConfigurationSetting = "MonitorRepairJobs";

        // ClusterObserver.
        public const string ClusterObserverName = "ClusterObserver";

        // Settings.
        public const string ClusterObserverConfigurationSectionName = "ClusterObserverConfiguration";
        public const string MaxTimeNodeStatusNotOkSetting = "MaxTimeNodeStatusNotOk";

        // Telemetry Settings Parameters.
        public const string TelemetryProviderType = "TelemetryProvider";
        public const string LogAnalyticsLogTypeParameter = "LogAnalyticsLogType";
        public const string LogAnalyticsSharedKeyParameter = "LogAnalyticsSharedKey";
        public const string LogAnalyticsWorkspaceIdParameter = "LogAnalyticsWorkspaceId";
        public const string InfrastructureServiceType = "InfrastructureServiceType";
        public const string ClusterTypeSfrp = "SFRP";
        public const string Undefined = "Undefined";
        public const string ClusterTypePaasV1 = "PaasV1";
        public const string ClusterTypeStandalone = "Standalone";
        public const string EnableETWProvider = "EnableETWProvider";
        public const string OperationalTelemetryEnabledParameter = "EnableOperationalTelemetry";
    }
}
