using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.MediaServices.Client;
using System.Configuration;

namespace DetectionApp
{
    public static class NotificationWebhook
    {
        internal const string SignatureHeaderKey = "sha256";
        internal const string SignatureHeaderValueTemplate = SignatureHeaderKey + "={0}";                

        [FunctionName("NotificationWebhook")]
        public static async Task<object> Run([HttpTrigger(WebHookType = "genericJson")]HttpRequestMessage req, TraceWriter log)
        {
            Guid requestId = Guid.NewGuid();
            log.Info($"Notification webhook started, request {requestId} RequestUri={req.RequestUri}");
            try
            {
                Task<byte[]> taskForRequestBody = req.Content.ReadAsByteArrayAsync();
                byte[] requestBody = await taskForRequestBody;

                string jsonContent = await req.Content.ReadAsStringAsync();
                log.Info($"Notification webhook request {requestId} Request Body = {jsonContent}");
                IEnumerable<string> values = null;
                if (req.Headers.TryGetValues("ms-signature", out values))
                {
                    byte[] signingKey = Convert.FromBase64String(FaceHelper.SigningKey);
                    string signatureFromHeader = values.FirstOrDefault();

                    if (VerifyWebHookRequestSignature(requestBody, signatureFromHeader, signingKey))
                    {
                        string requestMessageContents = Encoding.UTF8.GetString(requestBody);

                        NotificationMessage msg = JsonConvert.DeserializeObject<NotificationMessage>(requestMessageContents);

                        if (VerifyHeaders(req, msg, log))
                        {
                            string newJobStateStr = (string)msg.Properties.Where(j => j.Key == "NewState").FirstOrDefault().Value;
                            if (newJobStateStr == "Finished")
                            {
                                log.Info($"Notification webhook finished for request Id:{requestId}");
                                IJob job = FaceHelper.MediaContext.Jobs.Where(j => j.Id == msg.Properties["JobId"]).FirstOrDefault();
                                if (job != null)
                                {                                    
                                    if (job.OutputMediaAssets.Count > 0)
                                    {
                                        IAsset asset = job.OutputMediaAssets[job.OutputMediaAssets.Count -1]; //always we want to take the last output                                        
                                        string strStep = asset.Name.Substring(asset.Name.LastIndexOf('_') + 1);
                                        string jobName = asset.Name.Substring(0, asset.Name.LastIndexOf('_'));
                                        string videoname = jobName.Substring(0, jobName.LastIndexOf('_')) + ".mp4"; //TODO fix
                                        string requestid = jobName.Substring(jobName.LastIndexOf('_') + 1);                                        
                                        log.Info($"Notification webhook finished duration: {job.RunningDuration} for request Id:{requestId} details: {strStep} {jobName} {videoname}");
                                        VideoAnalysisSteps step;
                                        if (Enum.TryParse(strStep, out step))
                                        {
                                            HttpClient client = FaceHelper.GetHttpClientForVideo(log);
                                            VideoAnalysisSteps nextStep = VideoAnalysisSteps.Unknown;
                                            string containerName = string.Empty;
                                            switch (step)
                                            {
                                                case VideoAnalysisSteps.Encode:
                                                    nextStep = VideoAnalysisSteps.Redactor;
                                                    break;
                                                case VideoAnalysisSteps.Redactor:
                                                    containerName = ConfigurationManager.AppSettings["videoresultcontainername"];
                                                    nextStep = VideoAnalysisSteps.Copy;
                                                    break;                                                
                                                case VideoAnalysisSteps.LiveRedactor:
                                                    containerName = ConfigurationManager.AppSettings["streamresultcontainername"];
                                                    nextStep = VideoAnalysisSteps.Copy;
                                                    break;
                                            }
                                            if (nextStep != VideoAnalysisSteps.Unknown)
                                            {
                                                await new SendToEventGrid(log, client).SendVideoData(new VideoAssetData()
                                                {
                                                    RequestId = requestid,
                                                    AssetName = asset.Name,
                                                    JobName = jobName,
                                                    VideoName = videoname,
                                                    ContainerName = containerName,
                                                    Step = nextStep,
                                                    PrevStep = step,
                                                    TimeStamp = DateTime.UtcNow
                                                });

                                                //copy the subclip video to stream container in case it was set in the setting
                                                if ((step == VideoAnalysisSteps.LiveRedactor) && (ConfigurationManager.AppSettings["copysubclip"] == "1") &&
                                                    (job.OutputMediaAssets.Count > 1))
                                                {
                                                    await new SendToEventGrid(log, client).SendVideoData(new VideoAssetData()
                                                    {
                                                        RequestId = requestid,
                                                        AssetName = job.OutputMediaAssets[job.OutputMediaAssets.Count -2].Name, //assume that the second from the last item is the subclip output
                                                        JobName = jobName,
                                                        VideoName = videoname,
                                                        ContainerName = ConfigurationManager.AppSettings["streamsourcecontainername"],
                                                        Step = VideoAnalysisSteps.Copy,
                                                        PrevStep = VideoAnalysisSteps.Subclip,
                                                        TimeStamp = DateTime.UtcNow
                                                    });
                                                }
                                            }
                                            else
                                            {
                                                log.Info($"Notification webhookfor request Id:{requestId} didn't find next step: {nextStep}");
                                            }
                                        }
                                        else
                                        {
                                            log.Info($"Notification webhookfor request Id:{requestId} didn't cast step: {strStep}");
                                        }
                                    }
                                    else
                                    {
                                        log.Info($"Notification webhookfor request Id:{requestId} didn't find output asset for job: {job.Name}");
                                    }
                                }
                                else
                                {
                                    log.Info($"Notification webhookfor request Id:{requestId} didn't find jobid: {msg.Properties["JobId"]}");
                                }
                            }

                            return req.CreateResponse(HttpStatusCode.OK, string.Empty);
                        }
                        else
                        {
                            log.Info($"Notification webhook for VerifyHeaders request {requestId} failed.");
                            return req.CreateResponse(HttpStatusCode.BadRequest, "VerifyHeaders failed.");
                        }
                    }
                    else
                    {
                        log.Info($"Notification webhook for VerifyWebHookRequestSignature request {requestId} failed.");
                        return req.CreateResponse(HttpStatusCode.BadRequest, "VerifyWebHookRequestSignature failed.");
                    }
                }
            }
            catch(Exception ex)
            {
                log.Info($"Notification webhook for request Id:{requestId} failed with exception {ex.Message}");
                return req.CreateResponse(HttpStatusCode.BadRequest, "Exception Error " + ex.Message);
            }
            log.Info($"Notification webhook for request Id:{requestId} failed");
            return req.CreateResponse(HttpStatusCode.BadRequest, "Generic Error");
        }

        private static bool VerifyWebHookRequestSignature(byte[] data, string actualValue, byte[] verificationKey)
        {
            using (var hasher = new HMACSHA256(verificationKey))
            {
                byte[] sha256 = hasher.ComputeHash(data);
                string expectedValue = string.Format(CultureInfo.InvariantCulture, SignatureHeaderValueTemplate, ToHex(sha256));

                return (0 == String.Compare(actualValue, expectedValue, System.StringComparison.Ordinal));
            }
        }

        private static bool VerifyHeaders(HttpRequestMessage req, NotificationMessage msg, TraceWriter log)
        {
            bool headersVerified = false;

            try
            {
                IEnumerable<string> values = null;
                if (req.Headers.TryGetValues("ms-mediaservices-accountid", out values))
                {
                    string accountIdHeader = values.FirstOrDefault();
                    string accountIdFromMessage = msg.Properties["AccountId"];

                    if (0 == string.Compare(accountIdHeader, accountIdFromMessage, StringComparison.OrdinalIgnoreCase))
                    {
                        headersVerified = true;
                    }
                    else
                    {
                        log.Info($"accountIdHeader={accountIdHeader} does not match accountIdFromMessage={accountIdFromMessage}");
                    }
                }
                else
                {
                    log.Info($"Header ms-mediaservices-accountid not found.");
                }
            }
            catch (Exception e)
            {
                log.Info($"VerifyHeaders hit exception {e}");
                headersVerified = false;
            }

            return headersVerified;
        }

        private static readonly char[] HexLookup = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        private static string ToHex(byte[] data)
        {
            if (data == null)
            {
                return string.Empty;
            }

            char[] content = new char[data.Length * 2];
            int output = 0;
            byte d;

            for (int input = 0; input < data.Length; input++)
            {
                d = data[input];
                content[output++] = HexLookup[d / 0x10];
                content[output++] = HexLookup[d % 0x10];
            }

            return new string(content);
        }

        internal enum NotificationEventType
        {
            None = 0,
            JobStateChange = 1,
            NotificationEndPointRegistration = 2,
            NotificationEndPointUnregistration = 3,
            TaskStateChange = 4,
            TaskProgress = 5
        }

        internal sealed class NotificationMessage
        {
            public string MessageVersion { get; set; }
            public string ETag { get; set; }
            public NotificationEventType EventType { get; set; }
            public DateTime TimeStamp { get; set; }
            public IDictionary<string, string> Properties { get; set; }
        }
    }
}
