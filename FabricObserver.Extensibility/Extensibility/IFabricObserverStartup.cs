﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System.Fabric;
using Microsoft.Extensions.DependencyInjection;

namespace FabricObserver
{
    public interface IFabricObserverStartup
    {
        void ConfigureServices(IServiceCollection services, FabricClient fabricClient, StatelessServiceContext context);
    }
}
