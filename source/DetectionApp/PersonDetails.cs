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
    public class PersonDetails
    {
        private readonly HttpClient _client;
        private readonly TraceWriter _log;

        public PersonDetails(TraceWriter log, HttpClient client)
        {
            _log = log;
            _client = client;
        }

        public async Task<InternalPersonDetails> GetPersonName(string personId, string apiKey, string largegroupid, Guid requestId, PolicyWrap<HttpResponseMessage> policy)
        {
            return await MakePersonNameRequest(personId, apiKey, largegroupid, requestId, policy);
        }

        private async Task<InternalPersonDetails> MakePersonNameRequest(string personId, string apiKey, string largegroupid, Guid requestId, PolicyWrap<HttpResponseMessage> policy)
        {
            string strResult = string.Empty;
            _log.Info($"Making Person GET request requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}");
            InternalPersonDetails result = null;
            // Request parameters.
            string requestParameters = largegroupid + "/persons/" + personId;
            // Get the API URL and the API key from settings.
            var uriBase = ConfigurationManager.AppSettings["facePersonApiUrl"];            
            // Configure the HttpClient request headers.
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);            
            // Assemble the URI for the REST API Call.
            var uri = uriBase + "/" + requestParameters;
            try
            {
                // Execute the REST API call, implementing our resiliency strategy.
                HttpResponseMessage response = await policy.ExecuteAsync(() => _client.GetAsync(uri));

                // Get the JSON response.   
                strResult = await response.Content.ReadAsStringAsync();
                result = await response.Content.ReadAsAsync<InternalPersonDetails>();                
                _log.Info($"Person Details completed: {strResult} requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}");                
            }
            catch (BrokenCircuitException bce)
            {
                _log.Error($"Could not contact the Face API service due to the following error: {bce.Message} requestId: {requestId} apiKey:{apiKey} ticks: {DateTime.Now.Ticks}", bce);
            }
            catch (Exception e)
            {
                _log.Error($"Critical error-MakePersonNameRequest: {e.Message} requestId: {requestId} apiKey:{apiKey} string result:{strResult} ticks: {DateTime.Now.Ticks}", e);
            }

            return result;
        }


    }
}
