﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Fabric;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FabricObserver.Observers.Utilities
{
    public class WindowsInfoProvider : OperatingSystemInfoProvider
    {
        private const string TcpProtocol = "tcp";

        public override (long TotalMemory, double PercentInUse) TupleGetTotalPhysicalMemorySizeAndPercentInUse()
        {
            ManagementObjectSearcher win32OsInfo = null;
            ManagementObjectCollection results = null;
            long visibleTotal = -1;
            long freePhysical = -1;

            try
            {
                win32OsInfo = new ManagementObjectSearcher("SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
                results = win32OsInfo.Get();

                using (ManagementObjectCollection.ManagementObjectEnumerator enumerator = results.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        using (ManagementObject mObj = (ManagementObject)enumerator.Current)
                        {
                            PropertyDataCollection.PropertyDataEnumerator propEnumerator = mObj.Properties.GetEnumerator();

                            while (propEnumerator.MoveNext())
                            {
                                PropertyData prop = propEnumerator.Current;
                                string name = prop.Name;
                                string value = prop.Value.ToString();

                                if (name.Contains("TotalVisible"))
                                {
                                    visibleTotal = !string.IsNullOrWhiteSpace(value) ? long.Parse(value) : -1L;
                                }
                                else
                                {
                                    freePhysical = !string.IsNullOrWhiteSpace(value) ? long.Parse(value) : -1L;
                                }
                            }
                        }
                    }
                }

                if (visibleTotal == -1L || freePhysical == -1L)
                {
                    return (-1L, -1);
                }

                double used = ((double)(visibleTotal - freePhysical)) / visibleTotal;
                double usedPct = used * 100;

                return (visibleTotal / 1024 / 1024, Math.Round(usedPct, 2));
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is ManagementException)
            {
                Logger.LogWarning($"Handled failure in TupleGetTotalPhysicalMemorySizeAndPercentInUse:{Environment.NewLine}{e}");
            }
            finally
            {
                win32OsInfo?.Dispose();
                results?.Dispose();
            }

            return (-1L, -1);
        }

        public override (int LowPort, int HighPort) TupleGetDynamicPortRange()
        {
            using (var p = new Process())
            {
                try
                {
                    var ps = new ProcessStartInfo
                    {
                        Arguments = $"/c netsh int ipv4 show dynamicportrange {TcpProtocol} | find /i \"port\"",
                        FileName = $"{Environment.GetFolderPath(Environment.SpecialFolder.System)}\\cmd.exe",
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true
                    };

                    p.StartInfo = ps;
                    _ = p.Start();

                    var stdOutput = p.StandardOutput;
                    string output = stdOutput.ReadToEnd();
                    Match match = Regex.Match(
                                        output,
                                        @"Start Port\s+:\s+(?<startPort>\d+).+?Number of Ports\s+:\s+(?<numberOfPorts>\d+)",
                                        RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    p.WaitForExit();

                    string startPort = match.Groups["startPort"].Value;
                    string portCount = match.Groups["numberOfPorts"].Value;
                    int exitStatus = p.ExitCode;
                    stdOutput.Close();

                    if (exitStatus != 0)
                    {
                        return (-1, -1);
                    }

                    int lowPortRange = int.Parse(startPort);
                    int highPortRange = lowPortRange + int.Parse(portCount);

                    return (lowPortRange, highPortRange);
                }
                catch (Exception e) when (
                        e is ArgumentException
                        || e is IOException
                        || e is InvalidOperationException
                        || e is RegexMatchTimeoutException
                        || e is Win32Exception)
                {
                }
            }

            return (-1, -1);
        }

        /// <summary>
        /// Compute count of active TCP ports in dynamic range.
        /// </summary>
        /// <param name="processId">Optional: If supplied, then return the number of ephemeral ports in use by the process.</param>
        /// <param name="context">Optional (this is used by Linux callers only - see LinuxInfoProvider.cs): 
        /// If supplied, will use the ServiceContext to find the Linux Capabilities binary to run this command.</param>
        /// <returns>number of active Epehemeral TCP ports as int value</returns>
        public override int GetActiveEphemeralPortCount(int processId = -1, ServiceContext context = null)
        {
            int count;

            try
            {
                count = Retry.Do(() => GetTcpPortCount(processId, true), TimeSpan.FromSeconds(3), CancellationToken.None);
            }
            catch (AggregateException ae)
            {
                Logger.LogWarning($"Retry failed for GetActiveEphemeralPortCount:{Environment.NewLine}{ae}");
                count = -1;
            }

            return count;
        }

        /// <summary>
        /// Compute count of active TCP ports.
        /// </summary>
        /// <param name="processId">Optional: If supplied, then return the number of tcp ports in use by the process.</param>
        /// <param name="context">Optional (this is used by Linux callers only - see LinuxInfoProvider.cs): 
        /// If supplied, will use the ServiceContext to find the Linux Capabilities binary to run this command.</param>
        /// <returns>number of active TCP ports as int value</returns>
        public override int GetActiveTcpPortCount(int processId = -1, ServiceContext context = null)
        {
            int count;

            try
            {
                count = Retry.Do(() => GetTcpPortCount(processId), TimeSpan.FromSeconds(3), CancellationToken.None);
            }
            catch (AggregateException ae)
            {
                Logger.LogWarning($"Retry failed for GetActivePortCount:{Environment.NewLine}{ae}");
                count = -1;
            }

            return count;
        }

        public override Task<OSInfo> GetOSInfoAsync(CancellationToken cancellationToken)
        {
            ManagementObjectSearcher win32OsInfo = null;
            ManagementObjectCollection results = null;
            OSInfo osInfo = default;

            try
            {
                win32OsInfo = new ManagementObjectSearcher(
                                    "SELECT Caption,Version,Status,OSLanguage,NumberOfProcesses,FreePhysicalMemory,FreeVirtualMemory," +
                                            "TotalVirtualMemorySize,TotalVisibleMemorySize,InstallDate,LastBootUpTime FROM Win32_OperatingSystem");

                results = win32OsInfo.Get();

                using (ManagementObjectCollection.ManagementObjectEnumerator enumerator = results.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        try
                        {
                            using (ManagementObject mObj = (ManagementObject)enumerator.Current)
                            {
                                PropertyDataCollection.PropertyDataEnumerator propEnumerator = mObj.Properties.GetEnumerator();

                                while (propEnumerator.MoveNext())
                                {
                                    PropertyData prop = propEnumerator.Current;
                                    string name = prop.Name;
                                    string value = prop.Value?.ToString();

                                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                                    {
                                        continue;
                                    }

                                    switch (name.ToLowerInvariant())
                                    {
                                        case "caption":
                                            osInfo.Name = value;
                                            break;

                                        case "numberofprocesses":
                                            if (int.TryParse(value, out int numProcesses))
                                            {
                                                osInfo.NumberOfProcesses = numProcesses;
                                            }
                                            else
                                            {
                                                osInfo.NumberOfProcesses = -1;
                                            }

                                            break;

                                        case "status":
                                            osInfo.Status = value;
                                            break;

                                        case "oslanguage":
                                            osInfo.Language = value;
                                            break;

                                        case "version":
                                            osInfo.Version = value;
                                            break;

                                        case "installdate":
                                            osInfo.InstallDate = ManagementDateTimeConverter.ToDateTime(value).ToUniversalTime().ToString("o");
                                            break;

                                        case "lastbootuptime":
                                            osInfo.LastBootUpTime = ManagementDateTimeConverter.ToDateTime(value).ToUniversalTime().ToString("o");
                                            break;

                                        case "freephysicalmemory":
                                            osInfo.FreePhysicalMemoryKB = ulong.Parse(value);
                                            break;

                                        case "freevirtualmemory":
                                            osInfo.FreeVirtualMemoryKB = ulong.Parse(value);
                                            break;

                                        case "totalvirtualmemorysize":
                                            osInfo.TotalVirtualMemorySizeKB = ulong.Parse(value);
                                            break;

                                        case "totalvisiblememorysize":
                                            osInfo.TotalVisibleMemorySizeKB = ulong.Parse(value);
                                            break;
                                    }
                                }
                            }
                        }
                        catch (ManagementException me)
                        {
                            Logger.LogInfo($"Handled ManagementException in GetOSInfoAsync retrieval:{Environment.NewLine}{me}");
                        }
                    }
                }
            }
            finally
            {
                results?.Dispose();
                win32OsInfo?.Dispose();
            }

            return Task.FromResult(osInfo);
        }

        // Not implemented. No Windows support.
        public override int GetMaximumConfiguredFileHandlesCount()
        {
            return -1;
        }

        // Not implemented. No Windows support.
        public override int GetTotalAllocatedFileHandlesCount()
        {
            return -1;
        }

        private int GetTcpPortCount(int processId = -1, bool ephemeral = false)
        {
            var tempLocalPortData = new List<(int Pid, int Port)>();
            string findStrProc = string.Empty;
            string error = string.Empty;
            (int lowPortRange, int highPortRange) = (-1, -1);

            if (ephemeral)
            {
                (lowPortRange, highPortRange) = TupleGetDynamicPortRange();
            }

            if (processId > 0)
            {
                findStrProc = $" | find \"{processId}\"";
            }

            using (var p = new Process())
            {
                var ps = new ProcessStartInfo
                {
                    Arguments = $"/c netstat -qno -p {TcpProtocol}{findStrProc}",
                    FileName = $"{Environment.GetFolderPath(Environment.SpecialFolder.System)}\\cmd.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // Capture any error information from netstat.
                p.ErrorDataReceived += (sender, e) => { error += e.Data; };
                p.StartInfo = ps;
                _ = p.Start();
                var stdOutput = p.StandardOutput;

                // Start asynchronous read operation on error stream.  
                p.BeginErrorReadLine();

                string portRow;
                while ((portRow = stdOutput.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(portRow))
                    {
                        continue;
                    }

                    (int localPort, int pid) = TupleGetLocalPortPidPairFromNetStatString(portRow);

                    if (localPort == -1 || pid == -1)
                    {
                        continue;
                    }

                    if (processId > 0)
                    {
                        if (processId != pid)
                        {
                            continue;
                        }

                        // Only add unique pid (if supplied in call) and local port data to list.
                        if (tempLocalPortData.Any(t => t.Pid == pid && t.Port == localPort))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (tempLocalPortData.Any(t => t.Port == localPort))
                        {
                            continue;
                        }
                    }

                    // Ephemeral ports query?
                    if (ephemeral && (localPort < lowPortRange || localPort > highPortRange))
                    {
                        continue;
                    }
                    
                    tempLocalPortData.Add((pid, localPort));
                }

                p.WaitForExit();

                int exitStatus = p.ExitCode;
                int count = tempLocalPortData.Count;
                tempLocalPortData.Clear();
                stdOutput.Close();

                if (exitStatus == 0)
                {
                    return count;
                }

                // find will exit with a non-zero exit code if it doesn't find any matches in the case where a pid was supplied.
                // Do not throw in this case. 0 is the right answer.
                if (processId > 0 && error == string.Empty)
                {
                    return 0;
                }

                // there was an error associated with the non-zero exit code. Log it and throw.
                string msg = $"netstat -qno -p {TcpProtocol}{findStrProc} exited with {exitStatus}: {error}";
                Logger.LogWarning(msg);

                // this will be handled by Retry.Do().
                throw new Exception(msg);
            }
        }

        /// <summary>
        /// Gets local port number and associated process ID from netstat standard output line.
        /// </summary>
        /// <param name="netstatOutputLine">Single line (row) of text from netstat output.</param>
        /// <returns>Integer Tuple: (port, pid)</returns>
        private static (int, int) TupleGetLocalPortPidPairFromNetStatString(string netstatOutputLine)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(netstatOutputLine))
                {
                    return (-1, -1);
                }

                string[] stats = netstatOutputLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (stats.Length != 5 || !int.TryParse(stats[4], out int pid))
                {
                    return (-1, -1);
                }

                string localIpAndPort = stats[1];

                if (string.IsNullOrWhiteSpace(localIpAndPort) || !localIpAndPort.Contains(":"))
                {
                    return (-1, -1);
                }

                // We *only* care about the local IP.
                string localPort = localIpAndPort.Split(':')[1];

                if (!int.TryParse(localPort, out int port))
                {
                    return (-1, -1);
                }

                return (port, pid);
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException)
            {
                return (-1, -1);
            }
        }
    }
}
