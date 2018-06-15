using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DetectionApp;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Polly.Wrap;
using SimpleFaceDetect;
using StackExchange.Redis;

namespace DetectionApp
{
    public static class LoadPersonGroup
    {
        public static HttpClient _loadPersonClient;

        private static Lazy<ConnectionMultiplexer> lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
        {
            return ConnectionMultiplexer.Connect(ConfigurationManager.AppSettings["myRedis"]);
        });

        public static ConnectionMultiplexer Connection
        {
            get
            {
                return lazyConnection.Value;
            }
        }

        [FunctionName("LoadPersonGroup")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            Guid requestID = Guid.NewGuid();
            try
            {                
                log.Info($"LoadPersonGroup was triggered request id {requestID}");                
                string largepersongroupid = req.GetQueryNameValuePairs()
                    .FirstOrDefault(q => string.Compare(q.Key, "groupid", true) == 0)
                    .Value;
                if (largepersongroupid == null)
                {
                    // Get request body
                    dynamic data = await req.Content.ReadAsAsync<object>();
                    largepersongroupid = data?.groupid;
                }
                if (largepersongroupid != null)
                {
                    string apis = ConfigurationManager.AppSettings["apis"];
                    string[] apiArr = apis.Split(',');
                    if (apiArr.Length > 0)
                    {
                        
                        _loadPersonClient = _loadPersonClient ?? new HttpClient();
                        PolicyWrap<HttpResponseMessage> resilientPolicy = FaceHelper.DefineAndRetrieveResiliencyStrategy(log);
                        int i;
                        //setting the map of api to database in database 1                        
                        IDatabase cache = Connection.GetDatabase(1);
                        for (i = 1; i <= apiArr.Length; i++)
                        {
                            cache.StringSet(apiArr[i - 1], i + 1);
                        }
                        var personResult = new ListPersonGroup(log, _loadPersonClient);
                        for (i = 1; i <= apiArr.Length; i++)
                        {                            
                            bool isComplete = false;
                            string lastPerson = string.Empty;
                            Task<InternalPersonDetails[]> task;
                            List<InternalPersonDetails> personList = new List<InternalPersonDetails>();
                            cache = Connection.GetDatabase(i + 1);
                            while (!isComplete)
                            {
                                task = personResult.GetListPersonGroup(lastPerson, apiArr[i - 1], largepersongroupid, requestID, resilientPolicy);
                                task.Wait();
                                
                                if (task.Result.Length < 1000)
                                    isComplete = true;
                                if (task.Result.Length > 0)
                                {
                                    lastPerson = task.Result[task.Result.Length - 1].PersonId.ToString();
                                    personList.AddRange(task.Result.ToList());
                                }

                                int loop = task.Result.Length / 100;
                                var tmplist = task.Result.ToList();
                                int k = 0, len = 0;
                                for (k = 0; k < loop; k++)
                                {                                                                        
                                    var tmpinnerlist = tmplist.GetRange(k * 100, 100);
                                    len += tmpinnerlist.Count;
                                    cache.StringSet(tmpinnerlist.Select(s =>
                                            new KeyValuePair<RedisKey, RedisValue>(largepersongroupid + "-" + s.PersonId, s.Name)).ToArray(), When.Always);                                    
                                }
                                if (tmplist.Count - len > 0)
                                {
                                    var tmpinnerlist = tmplist.GetRange(k * 100, tmplist.Count - len);
                                    cache.StringSet(tmpinnerlist.Select(s =>
                                            new KeyValuePair<RedisKey, RedisValue>(largepersongroupid + "-" + s.PersonId, s.Name)).ToArray(), When.Always);
                                }                                

                            }
                        }
                        return req.CreateResponse(HttpStatusCode.OK, "Redis loaded successfully");
                    }
                    else
                    {
                        string msg = "List of Api setting is empty";
                        log.Error($"Error-LoadPersonGroup: {msg} requestId: {requestID} ticks: {DateTime.Now.Ticks}");
                        return req.CreateResponse(HttpStatusCode.BadRequest, "List of Api setting is empty");
                    }
                }
                else
                {
                    string msg = "Please pass a large person group id on the query string or in the request body";
                    log.Error($"Error-LoadPersonGroup: {msg} requestId: {requestID} ticks: {DateTime.Now.Ticks}");
                    return req.CreateResponse(HttpStatusCode.BadRequest, msg);
                }
            }
            catch(Exception e)
            {
                log.Error($"Exception-LoadPersonGroup: {e.Message} requestId: {requestID} ticks: {DateTime.Now.Ticks}", e);
                return req.CreateResponse(HttpStatusCode.BadRequest, e.Message);
            }
        }
    }
}
