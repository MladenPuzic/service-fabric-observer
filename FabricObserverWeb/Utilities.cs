﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System.Fabric.Description;
using System.Runtime.InteropServices;
using System.Security;

namespace FabricObserverWeb
{
    public static class Utilities
    {
        internal static string GetConfigurationSetting(
            ConfigurationSettings configurationSettings,
            string configurationSectionName,
            string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName) || configurationSettings == null)
            {
                return null;
            }

            if (!configurationSettings.Sections.Contains(configurationSectionName)
                || configurationSettings.Sections[configurationSectionName] == null)
            {
                return null;
            }

            if (!configurationSettings.Sections[configurationSectionName].Parameters.Contains(parameterName))
            {
                return null;
            }

            string parameterValue = configurationSettings.Sections[configurationSectionName].Parameters[parameterName].Value;

            if (!configurationSettings.Sections[configurationSectionName].Parameters[parameterName].IsEncrypted ||
                string.IsNullOrEmpty(parameterValue))
            {
                return parameterValue;
            }

            var paramValueAsCharArray = SecureStringToCharArray(
                configurationSettings.Sections[configurationSectionName].Parameters[parameterName].DecryptValue());

            return new string(paramValueAsCharArray);
        }

        private static char[] SecureStringToCharArray(SecureString secureString)
        {
            if (secureString == null)
            {
                return null;
            }

            char[] charArray = new char[secureString.Length];
            var ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);

            try
            {
                Marshal.Copy(ptr, charArray, 0, secureString.Length);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }

            return charArray;
        }

        internal static SecureString StringToSecureString(string value)
        {
            if (value == null)
            {
                return null;
            }

            var secureString = new SecureString();
            foreach (var c in value)
            {
                secureString.AppendChar(c);
            }

            return secureString;
        }
    }
}
