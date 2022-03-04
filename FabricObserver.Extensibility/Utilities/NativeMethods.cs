﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;

namespace FabricObserver.Observers.Utilities
{
    // TODO: Consider employing IDisposable pattern here so folks can call Dispose directly (or use using block).
    /// <summary>
    /// Native helper methods. Note that in order to use the process-related functions, you must create an instance of this type and then set it to null
    /// when you are done with it. 
    /// </summary>
    [SuppressUnmanagedCodeSecurity]
    public class NativeMethods
    {
        private readonly IntPtr handleToSnapshot = IntPtr.Zero;
        private readonly bool _cacheSnapshotHandle;
        private readonly Logger _logger = new Logger("NativeMethods");

        /// <summary>
        /// You must create an instance of this type to use the process-related functions GetChildProcesses and GetProcessThreadCount.
        /// If you want to cache process data in memory, important to do when you call these functions over and over again, concurrently,
        /// then pass cacheProcessData as true.
        /// </summary>
        /// <param name="cacheProcessData">Whether or not to cache process data in memory.</param>
        public NativeMethods(bool cacheProcessData)
        {
            if (cacheProcessData)
            {
                // Create a snapshot of all processes currently running on machine.
                handleToSnapshot = CreateToolhelp32Snapshot((uint)SnapshotFlags.Process | (uint)SnapshotFlags.NoHeaps, 0);

                // 0 or -1 mean failure.
                if (handleToSnapshot.ToInt32() < 1)
                {
                    string msg = $"NativeMethods initialization failure. Unable to create a process snapshot: {Marshal.GetLastWin32Error()}.";
                    _logger.LogWarning(msg);
               
                    throw new Win32Exception(msg);
                }
            }

            _cacheSnapshotHandle = cacheProcessData;   
        }

        [Flags]
        public enum MINIDUMP_TYPE
        {
            MiniDumpNormal = 0x00000000,
            MiniDumpWithDataSegs = 0x00000001,
            MiniDumpWithFullMemory = 0x00000002,
            MiniDumpWithHandleData = 0x00000004,
            MiniDumpFilterMemory = 0x00000008,
            MiniDumpScanMemory = 0x00000010,
            MiniDumpWithUnloadedModules = 0x00000020,
            MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
            MiniDumpFilterModulePaths = 0x00000080,
            MiniDumpWithProcessThreadData = 0x00000100,
            MiniDumpWithPrivateReadWriteMemory = 0x00000200,
            MiniDumpWithoutOptionalData = 0x00000400,
            MiniDumpWithFullMemoryInfo = 0x00000800,
            MiniDumpWithThreadInfo = 0x00001000,
            MiniDumpWithCodeSegs = 0x00002000,
            MiniDumpWithoutAuxiliaryState = 0x00004000,
            MiniDumpWithFullAuxiliaryState = 0x00008000,
            MiniDumpWithPrivateWriteCopyMemory = 0x00010000,
            MiniDumpIgnoreInaccessibleMemory = 0x00020000,
            MiniDumpWithTokenInformation = 0x00040000,
            MiniDumpWithModuleHeaders = 0x00080000,
            MiniDumpFilterTriage = 0x00100000,
            MiniDumpValidTypeFlags = 0x001fffff
        }

        [StructLayout(LayoutKind.Sequential)] 
        internal struct PROCESS_MEMORY_COUNTERS_EX
        {
            internal uint cb;
            internal uint PageFaultCount;
            internal IntPtr PeakWorkingSetSize;
            internal IntPtr WorkingSetSize;
            internal IntPtr QuotaPeakPagedPoolUsage;
            internal IntPtr QuotaPagedPoolUsage;
            internal IntPtr QuotaPeakNonPagedPoolUsage;
            internal IntPtr QuotaNonPagedPoolUsage;
            internal IntPtr PagefileUsage;
            internal IntPtr PeakPagefileUsage;
            internal IntPtr PrivateUsage;
        }

        //inner enum used only internally
        [Flags]
        private enum SnapshotFlags : uint
        {
            HeapList = 0x00000001,
            Process = 0x00000002,
            Thread = 0x00000004,
            Module = 0x00000008,
            Module32 = 0x00000010,
            Inherit = 0x80000000,
            All = 0x0000001F,
            NoHeaps = 0x40000000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            const int MAX_PATH = 260;
            internal uint dwSize;
            internal uint cntUsage;
            internal uint th32ProcessID;
            internal IntPtr th32DefaultHeapID;
            internal uint th32ModuleID;
            internal uint cntThreads;
            internal uint th32ParentProcessID;
            internal int pcPriClassBase;
            internal uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            internal string szExeFile;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MEMORYSTATUSEX
        {
            /// <summary>
            /// Size of the structure, in bytes. You must set this member before calling GlobalMemoryStatusEx.
            /// </summary>
            public uint dwLength;

            /// <summary>
            /// Number between 0 and 100 that specifies the approximate percentage of physical memory that is in use (0 indicates no memory use and 100 indicates full memory use).
            /// </summary>
            public uint dwMemoryLoad;

            /// <summary>
            /// Total size of physical memory, in bytes.
            /// </summary>
            public ulong ullTotalPhys;

            /// <summary>
            /// Size of physical memory available, in bytes.
            /// </summary>
            public ulong ullAvailPhys;

            /// <summary>
            /// Size of the committed memory limit, in bytes. This is physical memory plus the size of the page file, minus a small overhead.
            /// </summary>
            public ulong ullTotalPageFile;

            /// <summary>
            /// Size of available memory to commit, in bytes. The limit is ullTotalPageFile.
            /// </summary>
            public ulong ullAvailPageFile;

            /// <summary>
            /// Total size of the user mode portion of the virtual address space of the calling process, in bytes.
            /// </summary>
            public ulong ullTotalVirtual;

            /// <summary>
            /// Size of unreserved and uncommitted memory in the user mode portion of the virtual address space of the calling process, in bytes.
            /// </summary>
            public ulong ullAvailVirtual;

            /// <summary>
            /// Size of unreserved and uncommitted memory in the extended portion of the virtual address space of the calling process, in bytes.
            /// </summary>
            public ulong ullAvailExtendedVirtual;

            /// <summary>
            /// Initializes a new instance of the <see cref="T:MEMORYSTATUSEX"/> class.
            /// </summary>
            public MEMORYSTATUSEX()
            {
                this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr CreateToolhelp32Snapshot([In] uint dwFlags, [In] uint th32ProcessID);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool Process32First([In] IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool Process32Next([In] IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessMemoryInfo(IntPtr hProcess, [Out] out PROCESS_MEMORY_COUNTERS_EX counters, [In] uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetProcessHandleCount(IntPtr hProcess, out uint pdwHandleCount);

        // Process dump support.
        [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, SafeHandle hFile, MINIDUMP_TYPE dumpType, IntPtr expParam, IntPtr userStreamParam, IntPtr callbackParam);

        public static MEMORYSTATUSEX GetSystemMemoryInfo()
        {
            MEMORYSTATUSEX memory = new MEMORYSTATUSEX();

            if (GlobalMemoryStatusEx(memory))
            {
                return memory;
            }
            else
            {
                throw new Win32Exception(string.Format("NativeMethods.GetSystemMemoryInfo Failed with win32 error code {0}", Marshal.GetLastWin32Error()));
            }
        }

        /// <summary>
        /// Gets the child processes, if any, belonging to the process with supplied pid.
        /// </summary>
        /// <param name="parentpid">The process ID of parent process.</param>
        /// <returns>A List of tuple (string procName,  int procId) representing each child process.</returns>
        /// <exception cref="Win32Exception">A Win32 Error Code will be present in the exception Message.</exception>
        public List<(string procName, int procId)> GetChildProcesses(int parentpid)
        {
            List<(string procName, int procId)> childProcs = new List<(string procName, int procId)>();
            PROCESSENTRY32 procEntry = new PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32))
            };
            string[] ignoreProcessList = new string[] { "conhost.exe", "csrss.exe", "lsass.exe", "svchost.exe", "wininit.exe", "winlogon.exe" };
            IntPtr handleToSnapshot = IntPtr.Zero;

            try
            {
                if (!_cacheSnapshotHandle)
                {
                    handleToSnapshot = CreateToolhelp32Snapshot((uint)SnapshotFlags.Process | (uint)SnapshotFlags.NoHeaps, 0);
                }
                else
                {
                    handleToSnapshot = this.handleToSnapshot;
                }

                if (!Process32First(handleToSnapshot, ref procEntry))
                {
                    throw new Win32Exception($"NativeMethods.GetChildProcesses: Failed with win32 error code {Marshal.GetLastWin32Error()}");
                }

                do
                {
                    try
                    {
                        if (parentpid != procEntry.th32ParentProcessID)
                        {
                            continue;
                        }

                        if (ignoreProcessList.Contains(procEntry.szExeFile))
                        {
                            continue;
                        }

                        childProcs.Add((procEntry.szExeFile.Replace(".exe", ""), (int)procEntry.th32ProcessID));
                    }
                    catch (ArgumentException)
                    {

                    }

                } while (Process32Next(handleToSnapshot, ref procEntry));

                return childProcs;
            }
            finally
            {
                if (!_cacheSnapshotHandle)
                {
                    ReleaseHandle(handleToSnapshot);
                }
            }
        }

        /// <summary>
        /// Gets the number of execution threads started by the process with supplied pid.
        /// </summary>
        /// <param name="pid">The id of the process (pid).</param>
        /// <returns>The number of execution threads started by the process.</returns>
        /// <exception cref="Win32Exception">A Win32 Error Code will be present in the exception Message.</exception>
        public int GetProcessThreadCount(int pid)
        {
            int threadCount = 0;
            PROCESSENTRY32 procEntry = new PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32))
            };
            IntPtr handleToSnapshot = IntPtr.Zero;

            try
            {
                if (!_cacheSnapshotHandle)
                {
                    handleToSnapshot = CreateToolhelp32Snapshot((uint)SnapshotFlags.Process | (uint)SnapshotFlags.NoHeaps, 0);
                }
                else
                {
                    handleToSnapshot = this.handleToSnapshot;
                }

                if (!Process32First(handleToSnapshot, ref procEntry))
                {
                    throw new Win32Exception($"NativeMethods.GetProcessThreadCount: Failed with win32 error code {Marshal.GetLastWin32Error()}");
                }

                do
                {
                    if (pid == procEntry.th32ProcessID)
                    {
                        threadCount = (int)procEntry.cntThreads;
                        break;
                    }

                } while (Process32Next(handleToSnapshot, ref procEntry));

                return threadCount;
            }
            finally
            {
                if (!_cacheSnapshotHandle)
                {
                    ReleaseHandle(handleToSnapshot);
                }
            }
        }

        private void ReleaseHandle(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                if (!CloseHandle(handle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        ~NativeMethods()
        {
            ReleaseHandle(this.handleToSnapshot);
        }
    }
}
