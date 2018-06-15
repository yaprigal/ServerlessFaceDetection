using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Polly.CircuitBreaker;
using Polly.Wrap;
using SimpleFaceDetect;
using System;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace DetectionApp
{
    public class FaceDetect
    {
        private readonly HttpClient _client;
        private readonly TraceWriter _log;

        public FaceDetect(TraceWriter log, HttpClient client)
        {
            _log = log;
            _client = client;
        }

        public async Task<FaceDetectResult[]> DetectFaces(byte[] imageBytes, string apiKey, Guid requestId, PolicyWrap<HttpResponseMessage> policy)
        {
            return await MakeFaceDetectRequest(imageBytes, apiKey, requestId, policy);
        }

        private async Task<FaceDetectResult[]> MakeFaceDetectRequest(byte[] imageBytes, string apiKey, Guid requestId, PolicyWrap<HttpResponseMessage> policy)
        {
            _log.Info($"Making detect request requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}");
            string strResult = string.Empty;
            FaceDetectResult[] result = null;
            // Request parameters.
            const string requestParameters = "returnFaceId=true&returnFaceLandmarks=false";
            // Get the API URL and the API key from settings.
            var uriBase = ConfigurationManager.AppSettings["faceDetectApiUrl"];           
            // Configure the HttpClient request headers.
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
            //_client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Assemble the URI for the REST API Call.
            var uri = uriBase + "?" + requestParameters;
            try
            {
                // Execute the REST API call, implementing our resiliency strategy.
                HttpResponseMessage response = await policy.ExecuteAsync(() => _client.PostAsync(uri, FaceHelper.GetImageHttpContent(imageBytes)));

                // Get the JSON response.                    
                strResult = await response.Content.ReadAsStringAsync();
                result = await response.Content.ReadAsAsync<FaceDetectResult[]>();               
                _log.Info($"detect completed: {strResult} requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}");                
            }
            catch (BrokenCircuitException bce)
            {
                _log.Error($"Could not contact the Face API service due to the following error: {bce.Message} requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}", bce);
            }
            catch (Exception e)
            {
                _log.Error($"Critical error-MakeFaceDetectRequest: {e.Message} requestId: {requestId} apiKey:{apiKey} string result:{strResult} ticks: {DateTime.Now.Ticks}", e);
            }

            return result;
        }        


    }
}
