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
    public class ListPersonGroup
    {
        private readonly HttpClient _client;
        private readonly TraceWriter _log;

        public ListPersonGroup(TraceWriter log, HttpClient client)
        {
            _log = log;
            _client = client;
        }

        public async Task<InternalPersonDetails[]> GetListPersonGroup(string startPerson, string apiKey, string largegroupid, Guid requestId, PolicyWrap<HttpResponseMessage> policy)
        {
            return await MakeListPersonGroupRequest(startPerson, apiKey, largegroupid, requestId, policy);
        }

        private async Task<InternalPersonDetails[]> MakeListPersonGroupRequest(string startPerson, string apiKey, string largegroupid, Guid requestId, PolicyWrap<HttpResponseMessage> policy)
        {
            string strResult = string.Empty;
            _log.Info($"Making ListPerson GET request requestId: {requestId} apiKey:{apiKey} start person {startPerson} ticks: {DateTime.Now.Ticks}");
            InternalPersonDetails[] result = null;
            // Request parameters.
            string requestParameters = largegroupid + "/persons";
            // Get the API URL and the API key from settings.
            var uriBase = ConfigurationManager.AppSettings["facePersonApiUrl"];            
            // Configure the HttpClient request headers.
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);            
            // Assemble the URI for the REST API Call.
            var uri = uriBase + "/" + requestParameters;
            if (startPerson != string.Empty)
                uri += "?start=" + startPerson;
            try
            {
                // Execute the REST API call, implementing our resiliency strategy.
                HttpResponseMessage response = await policy.ExecuteAsync(() => _client.GetAsync(uri));

                // Get the JSON response.   
                strResult = await response.Content.ReadAsStringAsync();
                result = await response.Content.ReadAsAsync<InternalPersonDetails[]>();                
                _log.Info($"ListPersonGroup completed: requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}");                
            }
            catch (BrokenCircuitException bce)
            {
                _log.Error($"Could not contact the Face API service due to the following error: {bce.Message} requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}", bce);
            }
            catch (Exception e)
            {
                _log.Error($"Critical error-MakeListPersonGroupRequest: {e.Message} requestId: {requestId} apiKey:{apiKey} string result:{strResult} ticks: {DateTime.Now.Ticks}", e);
            }

            return result;
        }


    }
}
