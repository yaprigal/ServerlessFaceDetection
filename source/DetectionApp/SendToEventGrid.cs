using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

namespace DetectionApp
{
    public class SendToEventGrid
    {
        private readonly HttpClient _client;
        private readonly TraceWriter _log;

        public SendToEventGrid(TraceWriter log, HttpClient client)
        {
            _log = log;
            _client = client;
        }

        public async Task SendVideoData(VideoAssetData data)
        {            
            switch(data.Step)
            {
                case VideoAnalysisSteps.Encode:
				    await Send("encode", "FaceDetect/VideoService", data);
                    break;
                case VideoAnalysisSteps.Redactor:
                case VideoAnalysisSteps.LiveRedactor:
                    await Send("redactor", "FaceDetect/VideoService", data);
                    break;
                case VideoAnalysisSteps.Copy:                    
                    await Send("copy", "FaceDetect/VideoService", data);
                    break;
            }            
        }

        private async Task Send(string eventType, string subject, VideoAssetData data)
        {
            // Get the API URL and the API key from settings.
            var uri = ConfigurationManager.AppSettings["eventGridTopicEndpoint"];
            var key = ConfigurationManager.AppSettings["eventGridTopicKey"];

            _log.Info($"request {data.RequestId} Sending video {data.VideoName} information for further analysis to the {eventType} Event Grid type");

            var events = new List<Event<VideoAssetData>>
            {
                new Event<VideoAssetData>()
                {
                    Data = data,
                    EventTime = DateTime.UtcNow,
                    EventType = eventType,
                    Id = Guid.NewGuid().ToString(),
                    Subject = subject
                }
            };

            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add("aeg-sas-key", key);
            await _client.PostAsJsonAsync(uri, events);

            _log.Info($"Sent the orginial request id: {events[0].Data.RequestId} to the Event Grid topic");
        }
    }
}
