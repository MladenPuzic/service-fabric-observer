﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace FabricObserver.Observers.Utilities
{
    internal interface IProcessInfoProvider
    {
        float GetProcessPrivateWorkingSetInMB(int processId);
    }
}
