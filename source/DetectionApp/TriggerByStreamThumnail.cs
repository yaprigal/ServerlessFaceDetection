using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DetectionApp.CustomException;
using System;
using System.IO;
using System.Configuration;
using System.Threading.Tasks;

namespace DetectionApp
{
    public static class TriggerByStreamThumnail
    {
        [FunctionName("TriggerByStreamThumnail")]
        public static async Task Run([EventGridTrigger]JObject eventGridEvent,
            [Blob("{data.url}", FileAccess.Read, Connection = "myblobconn")]Stream incomingThumbnail,
            [DocumentDB("YOUR_COSMOS_DB_NAME", "YOUR_STREAN_COLLECTION_NAME", CreateIfNotExists = false, ConnectionStringSetting = "myCosmosDBConnection")] IAsyncCollector<object> outputItem,
            TraceWriter log)
        {
            Guid requestID = Guid.NewGuid();
            log.Info($"Start TriggerByStreamThumnail request id: {requestID} ticks: {DateTime.Now.Ticks}");
            string eventdetails = eventGridEvent.ToString();
            bool result = true;
            try
            {
                string url = eventGridEvent["data"]["url"].ToString();
                string[] splitted = url.Split('/');
                if (splitted.Length > 1)
                {
                    string name = splitted[splitted.Length - 1];
                    string videoname = splitted[splitted.Length - 2];
                    string[] splittedfilename = videoname.Split('-');
                    if (splittedfilename.Length == 2)
                    {
                        string source = splittedfilename[0];
                        result = await FaceHelper.RunDetect(requestID, ConfigurationManager.AppSettings["apis"], name, source, incomingThumbnail,
                            ConfigurationManager.AppSettings["streamsourcecontainername"],
                            ConfigurationManager.AppSettings["streamresultcontainername"], outputItem, log, videoname);
                    }
                    else
                    {
                        throw new IncorrectFileName(name);
                    }
                }
                else
                {
                    throw new IncorrectFileName(url);
                }
            }
            catch (Exception ex)
            {
                log.Error($"Exception Message: {ex.Message}, requestId: {requestID}, ticks: {DateTime.Now.Ticks}", ex);
                result = false;
            }
            string succeed = result ? "" : "unsuccessful";
            log.Info($"Finished {succeed} TriggerByStreamThumnail requestId: {requestID} details: {eventdetails} ticks: {DateTime.Now.Ticks}");
        }
    }
}
