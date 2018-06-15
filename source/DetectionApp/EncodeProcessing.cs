using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using Microsoft.WindowsAzure.MediaServices.Client;
using System.Configuration;
using System.Net.Http;
using System.Threading.Tasks;

namespace DetectionApp
{
    public static class EncodeProcessing
    {
        [FunctionName("EncodeProcessing")]
        public static async Task Run([EventGridTrigger]JObject eventGridEvent, TraceWriter log)
        {
            Guid newReqId = Guid.NewGuid();
            bool result = true;
            string reqId = string.Empty;
            try
            {                
                log.Info($"Start EncodeProcessing requestId: {newReqId} {eventGridEvent.ToString(Formatting.Indented)} ticks: {DateTime.Now.Ticks}");
                var eventGrid = eventGridEvent.ToObject<Event<VideoAssetData>>();
                reqId = eventGrid.Data.RequestId;
                IAsset asset = FaceHelper.GetAsset(eventGrid.Data.AssetName);
                if (asset != null)
                {
                    string assetName = eventGrid.Data.JobName + "_" + VideoAnalysisSteps.Encode;
                    await FaceHelper.EncodeToAdaptiveBitrateMP4Set(asset, assetName, ConfigurationManager.AppSettings["AMSPreset"]);
                    log.Info($"request {newReqId} for {reqId} started encode process, send event grid to start redactor process ticks: {DateTime.Now.Ticks}");                    
                }
                else
                {
                    log.Info($"request {newReqId} for {reqId} didn't start encode no related asset name {eventGrid.Data.AssetName} ticks: {DateTime.Now.Ticks}");
                }
            }
            catch(Exception ex)
            {
                log.Error($"Exception Message: {ex.Message}, requestId: {newReqId} for {reqId}, ticks: {DateTime.Now.Ticks}", ex);
                result = false;
            }
            string succeed = result ? "" : "unsuccessful";
            log.Info($"Finished {succeed} EncodeProcessing requestId: {newReqId} for {reqId} ticks: {DateTime.Now.Ticks}");
        }
    }
}
