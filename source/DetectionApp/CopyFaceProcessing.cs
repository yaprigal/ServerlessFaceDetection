using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System;
using System.Configuration;
using Microsoft.WindowsAzure.MediaServices.Client;
using System.Net.Http;
using System.IO;

namespace DetectionApp
{
    public static class CopyFaceProcessing
    {
        [FunctionName("CopyFaceProcessing")]
        public static async Task Run([EventGridTrigger]JObject eventGridEvent, TraceWriter log, ExecutionContext executionContext)
        {
            Guid newReqId = Guid.NewGuid();
            bool result = true;
            string reqId = string.Empty;
            try
            {
                log.Info($"Start CopyFaceProcessing requestId: {newReqId} {eventGridEvent.ToString(Formatting.Indented)} ticks: {DateTime.Now.Ticks}");
                var eventGrid = eventGridEvent.ToObject<Event<VideoAssetData>>();
                reqId = eventGrid.Data.RequestId;
                IAsset asset = FaceHelper.GetAsset(eventGrid.Data.AssetName);
                if (asset != null)
                {
                    Func<string, bool> op = FaceHelper.IsPictureType;
                    if (eventGrid.Data.PrevStep == VideoAnalysisSteps.Subclip)
                    {
                        op = FaceHelper.IsVideoType;
                    }                    
                    string container = eventGrid.Data.ContainerName;                    
                    string sourceConn = ConfigurationManager.AppSettings["myamsconn"];
                    string targetConn = ConfigurationManager.AppSettings["myblobconn"];                    
                    await FaceHelper.CopyFromAssetToBlob(asset, sourceConn, targetConn, container, eventGrid.Data.VideoName, op);
                    log.Info($"request {newReqId} for {reqId} completed copy process to {container} ticks: {DateTime.Now.Ticks}");                                        
                }
                else
                {
                    log.Info($"request {newReqId} for {reqId} didn't start copy no related asset name {eventGrid.Data.AssetName} ticks: {DateTime.Now.Ticks}");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Exception Message: {ex.Message}, requestId: {newReqId} for {reqId}, ticks: {DateTime.Now.Ticks}", ex);
                result = false;
            }
            string succeed = result ? "" : "unsuccessful";
            log.Info($"Finished {succeed} CopyFaceProcessing requestId: {newReqId} for {reqId} ticks: {DateTime.Now.Ticks}");
        }
    }
}
