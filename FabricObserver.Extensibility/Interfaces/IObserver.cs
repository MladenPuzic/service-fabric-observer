﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using FabricObserver.Observers.Utilities;

namespace FabricObserver.Observers.Interfaces
{
    /// <summary>
    /// OBSOLETE: Base Observer interface implemented by ObserverBase, the base type of all Observers.
    /// This is only still here because at one time it was used. This is no longer relevant and you should
    /// just derive from ObserverBase. There is no need for an interface for ObserverBase types.
    /// </summary>
    [Obsolete("This Interface is no longer used. It exists only in case there are still external usages.")]
    public interface IObserver : IDisposable
    {
        string ObserverName
        {
            get;
        }

        string NodeName
        {
            get; set;
        }

        Logger ObserverLogger
        {
            get; set;
        }

        DateTime LastRunDateTime
        {
            get; set;
        }

        TimeSpan RunInterval
        {
            get; set;
        }

        bool IsEnabled
        {
            get; set;
        }

        bool HasActiveFabricErrorOrWarning
        {
            get; set;
        }

        bool IsUnhealthy
        {
            get; set;
        }

        ConfigSettings ConfigurationSettings
        {
            get; set;
        }

        /// <summary>
        /// The function where observers observe.
        /// </summary>
        /// <param name="token">Cancellation token used to stop observers.</param>
        /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
        Task ObserveAsync(CancellationToken token);

        /// <summary>
        /// The function where observes report.
        /// </summary>
        /// <param name="token">Cancellation token used to stop observers.</param>
        /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
        Task ReportAsync(CancellationToken token);
    }
}