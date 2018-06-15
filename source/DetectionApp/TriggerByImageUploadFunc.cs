using DetectionApp.CustomException;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace DetectionApp
{
    public static class TriggerByImageUploadFunc
    {
        [FunctionName("TriggerByImageUploadFunc")]
        public static async Task Run([EventGridTrigger]JObject eventGridEvent,
            [Blob("{data.url}", FileAccess.Read, Connection = "myblobconn")]Stream incomingPicture,
            [DocumentDB("YOUR_COSMOS_DB_NAME", "YOUR_PHOTO_COLLECTION_NAME", CreateIfNotExists = false, ConnectionStringSetting = "myCosmosDBConnection")] IAsyncCollector<object> outputItem,
            TraceWriter log)
        {            
            Guid requestID = Guid.NewGuid();
            log.Info($"Start TriggerByImageUploadFunc request id: {requestID} ticks: {DateTime.Now.Ticks}");
            string eventdetails = eventGridEvent.ToString();
            bool result = true;
            try
            {
                string url = eventGridEvent["data"]["url"].ToString();
                string name = url.Substring(url.LastIndexOf('/') + 1);
                string[] splittedfilename = name.Split('-');
                if (splittedfilename.Length == 2)
                {
                    string source = splittedfilename[0];
                    result = await FaceHelper.RunDetect(requestID, ConfigurationManager.AppSettings["apis"], name, source, incomingPicture, ConfigurationManager.AppSettings["sourcecontainername"],
                        ConfigurationManager.AppSettings["resultcontainername"], outputItem, log);
                }
                else
                {
                    throw new IncorrectFileName(name);
                }
            }
            catch (Exception ex)
            {
                log.Error($"Exception Message: {ex.Message}, requestId: {requestID}, ticks: {DateTime.Now.Ticks}", ex);
                result = false;
            }
            string succeed = result ? "":"unsuccessful";
            log.Info($"Finished {succeed} TriggerByImageUploadFunc requestId: {requestID} details: {eventdetails} ticks: {DateTime.Now.Ticks}");            
        }
    }
}
