using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace DetectionApp
{
    public static class TriggerLiveStream
    {
        private static string storageAccountName = ConfigurationManager.AppSettings["myamsconn"];

        [FunctionName("TriggerLiveStream")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log,
            ExecutionContext execContext)
        {
            Guid requestID = Guid.NewGuid();
            log.Info($"Start TriggerLiveStream request id: {requestID} ticks: {DateTime.Now.Ticks}");                        
            int lastTableId = 0;
            int intervalsec = 60;
            TimeSpan starttime = TimeSpan.FromSeconds(0);            
            try
            {
                string triggerStart = DateTime.UtcNow.ToString("o");
                string jsonContent = await req.Content.ReadAsStringAsync();
                dynamic data = JsonConvert.DeserializeObject(jsonContent);
                log.Info($"TriggerLiveStream request id: {requestID} content: {jsonContent}");
                if (data.channelName == null || data.programName == null)
                {
                    log.Error($"TriggerLiveStream request id: {requestID} no channel name and program name in the input object");
                    return req.CreateResponse(HttpStatusCode.BadRequest, new
                    {
                        error = "Please pass channel name and program name in the input object (channelName, programName)"
                    });
                }
                if (data.intervalSec != null)
                {
                    intervalsec = (int)data.intervalSec;                   
                }
                string restApiEndpoint = ConfigurationManager.AppSettings["AMSRESTAPIEndpoint"];
                    
                // find the Channel, Program and Asset
                string channelName = (string)data.channelName;
                var channel = FaceHelper.MediaContext.Channels.Where(c => c.Name == channelName).FirstOrDefault();
                if (channel == null)
                {
                    log.Error($"TriggerLiveStream request id: {requestID} channel not found");
                    return req.CreateResponse(HttpStatusCode.BadRequest, new
                    {
                        error = "Channel not found"
                    });
                }
                string programName = (string)data.programName;
                var program = channel.Programs.Where(p => p.Name == programName).FirstOrDefault();
                if (program == null)
                {
                    log.Error($"TriggerLiveStream request id: {requestID} program not found");
                    return req.CreateResponse(HttpStatusCode.BadRequest, new
                    {
                        error = "Program not found"
                    });
                }

                string programState = program.State.ToString();
                string programid = program.Id;
                var asset = FaceHelper.GetAssetFromProgram(programid);
                if (asset == null)
                {
                    log.Info($"TriggerLiveStream Program asset not found for program {programid}");
                    return req.CreateResponse(HttpStatusCode.BadRequest, new
                    {
                        error = "Program asset not found"
                    });
                }
                log.Info($"TriggerLiveStream request id: {requestID} using program asset Id : {asset.Id}");
                CloudTable table = FaceHelper.GetCloudTable(ConfigurationManager.AppSettings["myamsconn"], "liveanalytics");
                var lastendtimeInTable = FaceHelper.RetrieveLastEndTime(table, programid);
                var assetmanifestdata = FaceHelper.GetManifestTimingData(asset, log);
                //log.Info($"request id: {requestID} timestamps: " + string.Join(",", assetmanifestdata.TimestampList.Select(n => n.ToString()).ToArray()));
                var livetime = TimeSpan.FromSeconds((double)assetmanifestdata.TimestampEndLastChunk / (double)assetmanifestdata.TimeScale);                
                starttime = FaceHelper.ReturnTimeSpanOnGOP(assetmanifestdata, livetime.Subtract(TimeSpan.FromSeconds(intervalsec)));
                log.Info($"TriggerLiveStream request id: {requestID} value starttime: {starttime} livetime: {livetime}");
                if (lastendtimeInTable != null)
                {
                    string lastProgramState = lastendtimeInTable.ProgramState;                    
                    var lastendtimeInTableValue = TimeSpan.Parse(lastendtimeInTable.LastEndTime);                    
                    lastTableId = int.Parse(lastendtimeInTable.Id);
                    log.Info($"TriggerLiveStream request id: {requestID} value id retrieved: {lastTableId} ProgramState: {lastProgramState} lastendtimeInTable: {lastendtimeInTableValue}");
                    if (lastendtimeInTableValue != null)
                    {
                        var delta = (livetime - lastendtimeInTableValue - TimeSpan.FromSeconds(intervalsec)).Duration();
                        log.Info($"TriggerLiveStream request id: {requestID} delta: {delta}");                        
                        if (delta < (TimeSpan.FromSeconds(3 * intervalsec))) // less than 3 times the normal duration 
                        {
                            starttime = lastendtimeInTableValue;
                            log.Info($"TriggerLiveStream request id: {requestID} value new starttime : {starttime}");
                        }
                    }
                }
                TimeSpan duration = livetime - starttime;
                log.Info($"TriggerLiveStream request id: {requestID} value duration: {duration}");
                string fileName = channelName + "-" + programName + "_" + requestID + ".mp4";
                string configurationSubclip = 
                    File.ReadAllText(Directory.GetParent(execContext.FunctionDirectory).FullName + "\\streamconfig.json").Replace("0:00:00.000000",
                        starttime.Subtract(TimeSpan.FromMilliseconds(100)).ToString()).Replace("0:00:30.000000", duration.Add(TimeSpan.FromMilliseconds(200)).ToString())
                        .Replace("ArchiveTopBitrate_{Basename}.mp4", fileName);                
                string configurationRedactor = File.ReadAllText(Directory.GetParent(execContext.FunctionDirectory).FullName + "\\config.json");

                int priority = 10;
                if (data.priority != null)
                {
                    priority = (int)data.priority;
                }
                string outputAssetSubclip = fileName + "_" + VideoAnalysisSteps.Subclip;
                string outputAssetLiveRedactor = fileName + "_" + VideoAnalysisSteps.LiveRedactor;
                await FaceHelper.RunSubclippingWithRedactor(asset, fileName, outputAssetSubclip, outputAssetLiveRedactor,
                    priority, configurationSubclip, configurationRedactor);
                log.Info($"TriggerLiveStream request id: {requestID} filename: {fileName} job Submitted");
                lastTableId++;
                FaceHelper.UpdateLastEndTime(table, starttime + duration, programid, lastTableId, program.State);
            }
            catch (Exception ex)
            {
                log.Error($"Exception Message: {ex.Message}, requestId: {requestID}, ticks: {DateTime.Now.Ticks}", ex);
                return req.CreateResponse(HttpStatusCode.BadRequest, "Completed with error sub cliping " + ex.Message);
            }
            return req.CreateResponse(HttpStatusCode.OK, "Complete OK sub cliping");
        }       
    }        
}


