using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Polly.CircuitBreaker;
using Polly.Wrap;
using SimpleFaceDetect;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace DetectionApp
{
    public class FaceIdentify
    {
        private readonly HttpClient _client;
        private readonly TraceWriter _log;

        public FaceIdentify(TraceWriter log, HttpClient client)
        {
            _log = log;
            _client = client;
        }

        public async Task<FaceDetectResult[]> IdentifyFaces(FaceIdentifyRequest req, string apiKey, Guid requestId, PolicyWrap<HttpResponseMessage> policy)
        {
            return await MakeFaceIdentifyRequest(req, apiKey, requestId, policy);
        }

        private async Task<FaceDetectResult[]> MakeFaceIdentifyRequest(FaceIdentifyRequest req, string apiKey, Guid requestId, PolicyWrap<HttpResponseMessage> policy)
        {
            string strResult = string.Empty;
            StringBuilder faceids = new StringBuilder();
            req.FaceIds.ToList().ForEach(s => faceids.Append(s + " "));
            _log.Info($"Making identify request faceids: {faceids} requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}");
            FaceDetectResult[] result = null;                        
            // Get the API URL and the API key from settings.
            var uri = ConfigurationManager.AppSettings["faceIdentifyApiUrl"];            
            // Configure the HttpClient request headers.
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
            //_client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));            
            try
            {
                // Execute the REST API call, implementing our resiliency strategy.
                HttpResponseMessage response = await policy.ExecuteAsync(() => _client.PostAsync(uri, FaceHelper.GetIdentifyHttpContent(req)));

                // Get the JSON response.         
                strResult = await response.Content.ReadAsStringAsync();
                result = await response.Content.ReadAsAsync<FaceDetectResult[]>();                
                _log.Info($"identify completed: {strResult} requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}");
                                
            }
            catch (BrokenCircuitException bce)
            {
                _log.Error($"Could not contact the Face API service due to the following error: {bce.Message} requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}", bce);
            }
            catch (Exception e)
            {
                _log.Error($"Critical error-MakeFaceIdentifyRequest: {e.Message} requestId: {requestId} apiKey:{apiKey} string result:{strResult} ticks: {DateTime.Now.Ticks}", e);
            }

            return result;
        }


    }
}
