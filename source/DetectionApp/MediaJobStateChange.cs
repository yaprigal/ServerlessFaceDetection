using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

namespace DetectionApp
{
    public static class MediaJobStateChange
    {
        /*  ONLY for demo purposes for Media service V3  integration with EventGrid*/
        [FunctionName("MediaJobStateChange")]
        public static async Task<object> Run([HttpTrigger(WebHookType = "genericJson")]HttpRequestMessage req, TraceWriter log)
        {            
            Guid requestID = Guid.NewGuid();
            log.Info($"MediaSerivces EventGrid Webhook was triggered,  request id: {requestID} ticks: {DateTime.Now.Ticks}");

            string jsonContent = await req.Content.ReadAsStringAsync();
            string eventGridValidation =
                req.Headers.FirstOrDefault(x => x.Key == "Aeg-Event-Type").Value?.FirstOrDefault();

            dynamic eventData = JsonConvert.DeserializeObject(jsonContent);

            log.Info($"request id: {requestID} event: {eventData}");

            if (eventGridValidation != string.Empty)
            {
                if (eventData[0].data.validationCode != string.Empty && eventData[0].eventType == "Microsoft.EventGrid.SubscriptionValidationEvent")
                {
                    return req.CreateResponse(HttpStatusCode.OK, new
                    {
                        validationResponse = eventData[0].data.validationCode
                    });
                }
            }
            log.Info(jsonContent);
            /*
             * for (int i = 0; i < eventData.Count; i++)
            {
                string state = eventData[i].data.state;
                //only in case it's job that was finished
                if (string.Compare(state, "Finished", true) == 0)
                {
                    string subject = eventData[i].subject;
                    string prevJobName = subject.Substring(subject.LastIndexOf("/") + 1);
                    if (string.IsNullOrEmpty(prevJobName) == false)
                    {
                        string adaptiveStreamingTransformName = ConfigurationManager.AppSettings["AdaptiveStreamingTransformName"];
                        string videoAnalyzerTransformName = ConfigurationManager.AppSettings["VideoAnalyzerTransformName"];
                        //start video analyze job
                        string uniqueness = Guid.NewGuid().ToString().Substring(0, 13);
                        //string videoName = FaceHelper.GetVideosNameFromJobName(prevJobName);
                        string videoName = "video1";
                        //string newJobName = FaceHelper.GenerateJobNameByUnique(videoName, uniqueness);
                        string newJobName = "job-" + uniqueness;
                        string outputAssetName = "output-" + uniqueness;
                        ConfigWrapper config = new ConfigWrapper();
                        IAzureMediaServicesClient client = FaceHelper.CreateMediaServicesClient(config);
                        if (subject.IndexOf(adaptiveStreamingTransformName) > -1)
                        {                                                        
                            string inputAssetName =
                                FaceHelper.GetJobOutputAssetName(client, config.ResourceGroup, config.AccountName, adaptiveStreamingTransformName, prevJobName);
                            JobInput jobInput = new JobInputAsset(assetName: inputAssetName);
                            Transform videoAnalyzerTransform =
                                FaceHelper.EnsureTransformExists(client, config.ResourceGroup, config.AccountName, videoAnalyzerTransformName,
                                new VideoAnalyzerPreset("en-US", audioInsightsOnly: false));
                            Asset outputAsset = FaceHelper.CreateOutputAsset(client, config.ResourceGroup, config.AccountName, outputAssetName);
                            Job job = FaceHelper.SubmitJob(client, config.ResourceGroup, config.AccountName, 
                                videoAnalyzerTransformName, newJobName, jobInput, outputAssetName);
                        }
                        else
                        {
                            if (subject.IndexOf(videoAnalyzerTransformName) > -1)
                            {
                                string inputAssetName = 
                                    FaceHelper.GetJobOutputAssetName(client, config.ResourceGroup, config.AccountName, videoAnalyzerTransformName, prevJobName);
                                string videoResultContainer = ConfigurationManager.AppSettings["videoresultcontainername"];
                                string resultBlobConnString = ConfigurationManager.AppSettings["myblobconn"];
                                await FaceHelper.CopyFromAssetToBlob(client, config.ResourceGroup, config.AccountName, inputAssetName,
                                    resultBlobConnString, videoResultContainer, videoName, "jpg");
                            }
                            else
                            {
                                log.Info($"request id: {requestID} element number: {i} not relevant subject: {subject}");
                            }
                        }
                    }
                    else
                    {
                        log.Info($"request id: {requestID} element number: {i} invalid job name {prevJobName} subject: {subject}");
                    }
                }
                else
                {
                    log.Info($"request id: {requestID} not coimpleted yet, state: {state}");
                }
            }                        
            */
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}
