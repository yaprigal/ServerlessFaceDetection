using DetectionApp.CustomException;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.MediaServices.Client;
using Newtonsoft.Json.Linq;
using Polly.Wrap;
using System;
using System.Configuration;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace DetectionApp
{
    public static class TriggerByVideoUploadFunc
    {
        [FunctionName("TriggerByVideoUploadFunc")]
        public static async Task Run([EventGridTrigger]JObject eventGridEvent,
            [Blob("{data.url}", FileAccess.Read, Connection = "myblobconn")]Stream incomingVideo,            
            TraceWriter log)
        {
            Guid requestID = Guid.NewGuid();
            log.Info($"Start TriggerByVideoUploadFunc request id: {requestID} ticks: {DateTime.Now.Ticks}");
            string eventdetails = eventGridEvent.ToString();
            bool result = true;
            try
            {
                string url = eventGridEvent["data"]["url"].ToString();
                string name = url.Substring(url.LastIndexOf('/') + 1);
                string nameWithoutExtension = name.Substring(0, name.IndexOf('.'));
                string[] splittedfilename = name.Split('-');
                if (splittedfilename.Length == 2)
                {
                    string jobName = nameWithoutExtension + "_" + requestID;
                    string assetName = nameWithoutExtension + "_" + requestID + "_" + VideoAnalysisSteps.Upload;
                    log.Info($"started uploaded new asset for {requestID} ticks: {DateTime.Now.Ticks}");
                    IAsset asset = FaceHelper.CreateAssetAndUploadSingleFile(incomingVideo, assetName, AssetCreationOptions.None);
                    log.Info($"completed uploaded asset for {requestID} send event grid to start encode process ticks: {DateTime.Now.Ticks}");
                    HttpClient client = FaceHelper.GetHttpClientForVideo(log);

                    await new SendToEventGrid(log, client).SendVideoData(new VideoAssetData()
                    {
                        RequestId = requestID.ToString(),
                        ContainerName = string.Empty,
                        AssetName = asset.Name,
                        JobName = jobName,
                        VideoName = name,
                        Step = VideoAnalysisSteps.Encode,
                        PrevStep = VideoAnalysisSteps.Upload,
                        TimeStamp = DateTime.UtcNow
                    });                    
                                        
                    /* API V3 Not working for now  */
                    //ConfigWrapper config = new ConfigWrapper();
                    //IAzureMediaServicesClient client = FaceHelper.CreateMediaServicesClient(config);
                    //client.LongRunningOperationRetryTimeout = 2;
                    //string uniqueness = Guid.NewGuid().ToString().Substring(0, 13);
                    //string jobName = FaceHelper.GenerateJobNameByUnique(nameWithoutExtension, uniqueness);                
                    //string jobName = "job-" + uniqueness;
                    //string outputAssetName = "output-" + uniqueness;
                    //string inputAssetName = "input-" + uniqueness;
                    //string adaptiveStreamingTransformName = ConfigurationManager.AppSettings["AdaptiveStreamingTransformName"];
                    //EncoderNamedPreset encodePreset = ConfigurationManager.AppSettings["EncoderPreset"];

                    //log.Info($"init job encode process {requestID} jobName: {jobName} input: {inputAssetName} output: {outputAssetName}  ticks: {DateTime.Now.Ticks}");
                    //Transform transform = FaceHelper.EnsureEncoderTransformExists(client, 
                    //    config.ResourceGroup, config.AccountName, adaptiveStreamingTransformName, encodePreset, log);
                    //log.Info($"after get transform for encode process {requestID} jobName: {jobName} ticks: {DateTime.Now.Ticks}");

                    //FaceHelper.CreateInputAsset(client, config.ResourceGroup, config.AccountName, inputAssetName, name, incomingVideo);
                    //log.Info($"after create input asset for encode process {requestID} jobName: {jobName} ticks: {DateTime.Now.Ticks}");
                    //JobInput jobInput = new JobInputAsset(assetName: inputAssetName);
                    //log.Info($"before create output asset for encode process {requestID} jobName: {jobName} ticks: {DateTime.Now.Ticks}");
                    //Asset outputAsset = FaceHelper.CreateOutputAsset(client, config.ResourceGroup, config.AccountName, outputAssetName);
                    //log.Info($"after create output asset for encode process {requestID} jobName: {jobName} input: {inputAssetName} output: {outputAssetName}");
                    //FaceHelper.SubmitJob(client, config.ResourceGroup, config.AccountName, 
                    //    adaptiveStreamingTransformName, jobName, jobInput, outputAssetName);                   
                    //log.Info($"after submit job encode process {requestID} jobName: {jobName} input: {inputAssetName} output: {outputAssetName} ticks: {DateTime.Now.Ticks}");                    
                }
                else
                {
                    throw new IncorrectFileName(name);
                }                
            }
            catch (Exception ex)
            {
                log.Error($"Exception Message: {ex.Message}, requestId: {requestID}, details: {eventdetails}, ticks: {DateTime.Now.Ticks}", ex);
                result = false;
            }
            string succeed = result ? "" : "unsuccessful";
            log.Info($"Finished {succeed} TriggerByVideoUploadFunc requestId: {requestID} ticks: {DateTime.Now.Ticks}");
        }
    }


}
