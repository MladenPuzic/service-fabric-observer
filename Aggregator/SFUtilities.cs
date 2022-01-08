﻿using Aggregator.Data;
using FabricObserver.Observers.MachineInfoModel;
using FabricObserver.Observers.Utilities;
using FabricObserver.Utilities.ServiceFabric;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Query;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static System.Fabric.FabricClient;

namespace Aggregator
{
    public class SFUtilities
    {
        public static readonly double intervalMiliseconds = 10000.00;

        private static SFUtilities instance;
        private static readonly object lockObj = new object();
        private FabricClient fabricClient;
        private QueryClient queryManager;

        protected SFUtilities() {
            fabricClient = new FabricClient();
            queryManager = fabricClient.QueryManager;
          
        }
        /// <summary>
        /// </summary>
        /// <returns>(totalMiliseconds, delta - time left to sleep to wakeup for Aggregating interval)</returns>
        public static (double totalMiliseconds, double delta)TupleGetTime(){
            var localTime = DateTime.Now;
            var timeSpan = TimeSpan.FromTicks(localTime.Ticks);
            double totalMiliseconds = timeSpan.TotalMilliseconds;
            double remainder = totalMiliseconds % SFUtilities.intervalMiliseconds;
            double delta = SFUtilities.intervalMiliseconds - remainder;
            return (totalMiliseconds, delta);
        }

        public static SFUtilities Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (lockObj)
                    {
                        if (instance == null)
                        {
                            instance = new SFUtilities();
                        }
                    }
                }

                return instance;
            }
        }

        public async Task<Counts> GetDeployedCountsAsync()
        {
            ApplicationList appList = await queryManager.GetApplicationListAsync();
            Uri appUri;
            int primaryCount = 0;
            int replicaCount = 0; //Primary + secondary for statefull services
            int instanceCount = 0; //Replica count for stateless services
            int count = 0; //replicaCount + instanceCount
            foreach (Application app in appList)
            {
                //appUri= app.ApplicationName.AbsoluteUri;
                appUri = app.ApplicationName;
                ServiceList servicesList = await queryManager.GetServiceListAsync(appUri);
                foreach (Service service in servicesList)
                {
                    var serviceUri = service.ServiceName;
                    var partitionList = await queryManager.GetPartitionListAsync(serviceUri);
                    bool isStatefull = typeof(StatefulService).IsInstanceOfType(service);
                    primaryCount += isStatefull ? partitionList.Count : 0;
                    foreach (var partition in partitionList)
                    {
                        int cnt = (await fabricClient.QueryManager.GetReplicaListAsync(partition.PartitionInformation.Id)).Where(r => r.ReplicaStatus == ServiceReplicaStatus.Ready).Count();
                        if (isStatefull) replicaCount += cnt;
                        else instanceCount += cnt;
                    }
                }
                
            }
            count = instanceCount + replicaCount;
            return new Counts(primaryCount, replicaCount, instanceCount, count);
        }
        
        public async Task<NodeList> GetNodeListAsync()
        {
            return await queryManager.GetNodeListAsync();
            
        }
        /// <summary>
        /// Return a Dictionary where keys are PIDs
        /// </summary>
        /// <param name="NodeName"></param>
        /// <returns></returns>
        public async Task<Dictionary<int,ProcessData>> GetDeployedProcesses(string NodeName,ServiceContext serviceContext,CancellationToken token)
        {
            
            Dictionary<int, ProcessData> pid=new Dictionary<int, ProcessData>();
            Client client = new Client(fabricClient, serviceContext);
            List<ReplicaOrInstanceMonitoringInfo> replicaList = await client.GetAllLocalReplicasOrInstances(token);
            foreach(var replica in replicaList)
            {
                int instanceCount = 0;
                int replicaCount = 0;
                int primaryCount = 0;

                if (replica.ServiceKind == ServiceKind.Stateful)
                {
                    replicaCount++;
                    if (replica.ReplicaRole == ReplicaRole.Primary)
                    {
                        primaryCount++;
                    }
                }
                else instanceCount++;

                int id=(int)replica.HostProcessId;

                ProcessData processData = null;
                //replica.ServiceName
                if (pid.ContainsKey(id))
                {
                    processData = pid[id];    
                }
                else
                {
                    processData = new ProcessData(id);
                    pid.Add(id, processData);
                    //Extend ChildProcess list only once
                    if(replica.ChildProcesses!=null)processData.ChildProcesses.AddRange(replica.ChildProcesses);

                }
                processData.serviceUris.Add(replica.ServiceName);
                processData.allCounts.PrimaryCount += primaryCount;
                processData.allCounts.ReplicaCount += replicaCount;
                processData.allCounts.InstanceCount += instanceCount;
                processData.allCounts.Count++;

                //for a shared process this may not be correct -> each service has the same processID so child processes are the same so we should overide not extend? 
                //processData.ChildProcesses.AddRange(replica.ChildProcesses);

                
            }
            
            return pid;
        }
        public async Task<(double cpuPercentage, float ramMB)> TupleGetResourceUsageForProcess(int pid)
        {
            var cpuUsage = new CpuUsage();
            double cpu=cpuUsage.GetCpuUsagePercentageProcess((int) pid);

            float ramMb = ProcessInfoProvider.Instance.GetProcessWorkingSetMb((int)pid);

            return (cpu, ramMb);
        }

    }
}
