using DetectionApp.CustomException;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.DataMovement;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Polly;
using Polly.Wrap;
using SimpleFaceDetect;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Xml.Linq;

namespace DetectionApp
{
    public class FaceHelper
    {
        public static Random Instance = new Random();        

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

        private static Lazy<CloudMediaContext> lazyMediaContext = new Lazy<CloudMediaContext>(() =>
        {
            string _AADTenantDomain = ConfigurationManager.AppSettings["AMSAADTenantDomain"];
            string _RESTAPIEndpoint = ConfigurationManager.AppSettings["AMSRESTAPIEndpoint"];
            string _AMSClientId = ConfigurationManager.AppSettings["AMSClientId"];
            string _AMSClientSecret = ConfigurationManager.AppSettings["AMSClientSecret"];
            AzureAdTokenCredentials tokenCredentials =
                new AzureAdTokenCredentials(_AADTenantDomain,
                new AzureAdClientSymmetricKey(_AMSClientId, _AMSClientSecret),
                AzureEnvironments.AzureCloudEnvironment);
            var tokenProvider = new AzureAdTokenProvider(tokenCredentials);
            return new CloudMediaContext(new Uri(_RESTAPIEndpoint), tokenProvider);            
        });

        public static CloudMediaContext MediaContext
        {
            get
            {
                return lazyMediaContext.Value;
            }
        }

        public static string WebHookEndpoint = ConfigurationManager.AppSettings["NotificationWebHookEndpoint"];
        public static string SigningKey = ConfigurationManager.AppSettings["NotificationSigningKey"];

        public static INotificationEndPoint NotificationEndPoint
        {
            get
            {
                var existingEndpoint = lazyMediaContext.Value.NotificationEndPoints.Where(e => e.Name == "FunctionWebHook").FirstOrDefault();
                INotificationEndPoint endpoint = null;

                if (existingEndpoint != null)
                {                    
                    endpoint = (INotificationEndPoint)existingEndpoint;
                }
                else
                {
                    byte[] keyBytes = Convert.FromBase64String(SigningKey);
                    endpoint = MediaContext.NotificationEndPoints.Create("FunctionWebHook", NotificationEndPointType.WebHook, WebHookEndpoint, keyBytes);                    
                }
                return endpoint;
            }
        }

        public static ConcurrentDictionary<string, Tuple<HttpClient, PolicyWrap<HttpResponseMessage>>> HttpClientList =
            new ConcurrentDictionary<string, Tuple<HttpClient, PolicyWrap<HttpResponseMessage>>>();

        public static IAzureMediaServicesClient CreateMediaServicesClient(ConfigWrapper config)
        {
            ArmClientCredentials credentials = new ArmClientCredentials(config);

            return new AzureMediaServicesClient(config.ArmEndpoint, credentials)
            {
                SubscriptionId = config.SubscriptionId,
            };
        }

        public static Transform EnsureEncoderTransformExists(IAzureMediaServicesClient client, string resourceGroupName, 
            string accountName, string transformName, EncoderNamedPreset preset, TraceWriter log)
        {                  
            Transform transform = client.Transforms.Get(resourceGroupName, accountName, transformName);
            if (transform == null)
            {
                TransformOutput[] output = new TransformOutput[]
                {
                    new TransformOutput
                    {
                        Preset = new BuiltInStandardEncoderPreset()
                        {
                            PresetName = preset
                        }
                    }
                };
                transform = client.Transforms.CreateOrUpdate(resourceGroupName, accountName, transformName, output);
            }
            return transform;
        }
        public static Transform EnsureTransformExists(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string transformName, Preset preset)
        {
            // Does a Transform already exist with the desired name? Assume that an existing Transform with the desired name
            // also uses the same recipe or Preset for processing content.
            Transform transform = client.Transforms.Get(resourceGroupName, accountName, transformName);

            if (transform == null)
            {
                // Start by defining the desired outputs.
                TransformOutput[] outputs = new TransformOutput[]
                {
                    new TransformOutput(preset),
                };

                // Create the Transform with the output defined above
                transform = client.Transforms.CreateOrUpdate(resourceGroupName, accountName, transformName, outputs);
            }

            return transform;
        }
        public static Asset CreateOutputAsset(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string assetName)
        {
            // Check if an Asset already exists
            Asset outputAsset = client.Assets.Get(resourceGroupName, accountName, assetName);
            Asset asset = new Asset();
            string outputAssetName = assetName;

            if (outputAsset != null)
            {
                // Name collision! TODO: maybe better handling 
                string uniqueness = @"-" + Guid.NewGuid().ToString();
                outputAssetName += uniqueness;
            }

            return client.Assets.CreateOrUpdate(resourceGroupName, accountName, outputAssetName, asset);
        }

        public static Asset CreateInputAsset(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string assetName,
            string blobName, Stream stream)
        {           
            Asset asset = client.Assets.CreateOrUpdate(resourceGroupName, accountName, assetName, new Asset());
            var response = client.Assets.ListContainerSas(resourceGroupName, accountName, assetName, permissions: AssetContainerPermission.ReadWrite,
                  expiryTime: DateTime.UtcNow.AddHours(1).ToUniversalTime()
              );
            var sasUri = new Uri(response.AssetContainerSasUrls.First());            
            CloudBlobContainer container = new CloudBlobContainer(sasUri);
            var blob = container.GetBlockBlobReference(blobName);            
            Task t = blob.UploadFromStreamAsync(stream);
            t.Wait();
            return asset;
        }

        public static string GetJobOutputAssetName(IAzureMediaServicesClient client, string resourceGroupName, string accountName,
            string transformName, string jobName)
        {
            Job job = client.Jobs.Get(resourceGroupName, accountName, transformName, jobName);
            JobOutputAsset outputAsset = (JobOutputAsset)job.Outputs[0];            
            return outputAsset.AssetName;                   
        }

        public static string GenerateJobNameByUnique(string name, string uniqueness)
        {
            return "job_" + name + "_" + uniqueness;
        }

        public static string GetVideosNameFromJobName(string name)
        {
            int start = name.IndexOf("_");
            int end = name.LastIndexOf("_");
            if ((start > -1) && (end > -1) && (end > start))
                return name.Substring(start + 1, end - start - 1);
             return string.Empty;
        }

        public static CloudTable GetCloudTable(string blobConn, string tableName)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(blobConn);                
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();            
            CloudTable table = tableClient.GetTableReference(tableName);

            return table;
        }

        public static IAsset GetAssetFromProgram(string programId)
        {
            IAsset asset = null;
            IProgram program = MediaContext.Programs.Where(p => p.Id == programId).FirstOrDefault();
            if (program != null)
            {
                asset = program.Asset;
            }
            return asset;
        }

        public static Uri GetValidOnDemandURI(IAsset asset, string preferredSE = null)
        {
            var aivalidurls = GetValidURIs(asset, preferredSE);
            if (aivalidurls != null)
            {
                return aivalidurls.FirstOrDefault();
            }
            else
            {
                return null;
            }
        }

        public static IEnumerable<Uri> GetValidURIs(IAsset asset, string preferredSE = null)
        {
            IEnumerable<Uri> ValidURIs;
            var ismFile = asset.AssetFiles.AsEnumerable().Where(f => f.Name.EndsWith(".ism")).OrderByDescending(f => f.IsPrimary).FirstOrDefault();
            if (ismFile != null)
            {
                var locators = asset.Locators.Where(l => l.Type == LocatorType.OnDemandOrigin && l.ExpirationDateTime > DateTime.UtcNow).OrderByDescending(l => l.ExpirationDateTime);
                var se = MediaContext.StreamingEndpoints.AsEnumerable().Where(o =>
                    (string.IsNullOrEmpty(preferredSE) || (o.Name == preferredSE))
                    &&
                    (!string.IsNullOrEmpty(preferredSE) || ((o.State == StreamingEndpointState.Running) && (CanDoDynPackaging(o))))).OrderByDescending(o => o.CdnEnabled);
                if (se.Count() == 0) // No running which can do dynpackaging SE and if not preferredSE. Let's use the default one to get URL
                {
                    se = MediaContext.StreamingEndpoints.AsEnumerable().Where(o => o.Name == "default").OrderByDescending(o => o.CdnEnabled);
                }
                var template = new UriTemplate("{contentAccessComponent}/{ismFileName}/manifest");
                ValidURIs = locators.SelectMany(l =>
                    se.Select(
                            o =>
                                template.BindByPosition(new Uri("https://" + o.HostName), l.ContentAccessComponent,
                                    ismFile.Name)))
                    .ToArray();
                return ValidURIs;
            }
            else
            {
                return null;
            }
        }

        public static string ReturnContent(IAssetFile assetFile)
        {
            string datastring = null;
            
            string tempPath = System.IO.Path.GetTempPath();
            string filePath = Path.Combine(tempPath, assetFile.Name);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            assetFile.Download(filePath);

            StreamReader streamReader = new StreamReader(filePath);
            Encoding fileEncoding = streamReader.CurrentEncoding;
            datastring = streamReader.ReadToEnd();
            streamReader.Close();
            File.Delete(filePath);            

            return datastring;
        }

        public static Uri GetValidOnDemandPath(IAsset asset, string preferredSE = null)
        {
            var aivalidurls = GetValidPaths(asset, preferredSE);
            if (aivalidurls != null)
            {
                return aivalidurls.FirstOrDefault();
            }
            else
            {
                return null;
            }
        }

        public static IEnumerable<Uri> GetValidPaths(IAsset asset, string preferredSE = null)
        {
            IEnumerable<Uri> ValidURIs;

            var locators = asset.Locators.Where(l => l.Type == LocatorType.OnDemandOrigin && l.ExpirationDateTime > DateTime.UtcNow).OrderByDescending(l => l.ExpirationDateTime);

            //var se = _context.StreamingEndpoints.AsEnumerable().Where(o => (o.State == StreamingEndpointState.Running) && (CanDoDynPackaging(o))).OrderByDescending(o => o.CdnEnabled);

            var se = MediaContext.StreamingEndpoints.AsEnumerable().Where(o =>

                   (string.IsNullOrEmpty(preferredSE) || (o.Name == preferredSE))
                   &&
                   (!string.IsNullOrEmpty(preferredSE) || ((o.State == StreamingEndpointState.Running) && (CanDoDynPackaging(o)))
                                                                           ))
               .OrderByDescending(o => o.CdnEnabled);

            if (se.Count() == 0) // No running which can do dynpackaging SE and if not preferredSE. Let's use the default one to get URL
            {
                se = MediaContext.StreamingEndpoints.AsEnumerable().Where(o => o.Name == "default").OrderByDescending(o => o.CdnEnabled);
            }

            var template = new UriTemplate("{contentAccessComponent}/");
            ValidURIs = locators.SelectMany(l => se.Select(
                        o =>
                            template.BindByPosition(new Uri("https://" + o.HostName), l.ContentAccessComponent)))
                .ToArray();

            return ValidURIs;
        }

        public static StreamEndpointType ReturnTypeSE(IStreamingEndpoint mySE)
        {
            if (mySE.ScaleUnits != null && mySE.ScaleUnits > 0)
            {
                return StreamEndpointType.Premium;
            }
            else
            {
                if (new Version(mySE.StreamingEndpointVersion) == new Version("1.0"))
                {
                    return StreamEndpointType.Classic;
                }
                else
                {
                    return StreamEndpointType.Standard;
                }
            }
        }

        public static bool CanDoDynPackaging(IStreamingEndpoint mySE)
        {
            return ReturnTypeSE(mySE) != StreamEndpointType.Classic;
        }

        public enum StreamEndpointType
        {
            Classic = 0,
            Standard,
            Premium
        }

        public static async Task CopyFromAssetToBlob(IAzureMediaServicesClient client, string resourceGroup, string accountName,
          string assetName, string resultBlobConnString, string resultContainer, string resultsFolder, string filter)
        {
            AssetContainerSas assetContainerSas = client.Assets.ListContainerSas(resourceGroup, accountName, assetName, permissions: AssetContainerPermission.Read,
                    expiryTime: DateTime.UtcNow.AddHours(1).ToUniversalTime());
            Uri containerSasUrl = new Uri(assetContainerSas.AssetContainerSasUrls.FirstOrDefault());
            CloudBlobContainer container = new CloudBlobContainer(containerSasUrl);
            CloudStorageAccount targertAccount = CloudStorageAccount.Parse(resultBlobConnString);
            var targetClient = targertAccount.CreateCloudBlobClient();
            var targetContainer = targetClient.GetContainerReference(resultContainer);
            var directory = targetContainer.GetDirectoryReference(resultsFolder);
            
            string filename = string.Empty;
            List<Task> tasks = new List<Task>();
            foreach (var blobItem in
                container.ListBlobs(null, true, BlobListingDetails.None).OfType<CloudBlockBlob>().Where(b => b.Name.EndsWith(filter)))
            {
                CloudBlockBlob destBlockBlob = directory.GetBlockBlobReference(blobItem.Name);
                tasks.Add(TransferManager.CopyAsync(blobItem, destBlockBlob, true));                                
            }
            await Task.WhenAll(tasks);                        
        }

        public static bool IsPictureType(string mimeType)
        {
            switch (mimeType)
            {
                case "image/bmp":
                case "image/jpeg":
                case "image/x-png":
                case "image/png":
                case "image/gif":
                    return true;
            }
            return false;
        }

        public static bool IsVideoType(string mimeType)
        {
            switch (mimeType)
            {
                case "video/mp4":                
                    return true;
            }
            return false;
        }

        public static async Task CopyFromAssetToBlob(IAsset asset, string sourceConn, string targetConn,string targetContainer, string targetDirectory, 
            Func<string, bool> action = null)
        {
            int skipSize = 0;
            int batchSize = 1000;
            int currentBatch = 0;
            int i = 0;
            int len = asset.AssetFiles.Count();

            CloudStorageAccount sourceAccount = CloudStorageAccount.Parse(sourceConn);
            CloudStorageAccount targertAccount = CloudStorageAccount.Parse(targetConn);

            var sourceClient = sourceAccount.CreateCloudBlobClient();
            var sourceContainerUri = asset.Uri.ToString();
            var sourceContainer = sourceClient.GetContainerReference(sourceContainerUri.Substring(sourceContainerUri.LastIndexOf('/') + 1));
            var targetClient = targertAccount.CreateCloudBlobClient();
            var targetContainerBlob = targetClient.GetContainerReference(targetContainer);
            var directory = targetContainerBlob.GetDirectoryReference(targetDirectory);
            
            List<Task> tasks = new List<Task>();
            while (true)
            {
                // Loop through all Jobs (1000 at a time) in the Media Services account
                IQueryable _assetCollectionQuery = asset.AssetFiles.Skip(skipSize).Take(batchSize);
                foreach (IAssetFile assetFile in _assetCollectionQuery)
                {
                    bool isOk = true;
                    if (action != null)
                        isOk = action(assetFile.MimeType);
                    if (isOk)
                    {
                        CloudBlockBlob sourceBlockBlob = sourceContainer.GetBlockBlobReference(assetFile.Name);
                        CloudBlockBlob destBlockBlob = directory.GetBlockBlobReference(assetFile.Name);
                        tasks.Add(TransferManager.CopyAsync(sourceBlockBlob, destBlockBlob, true));
                        currentBatch++;
                        i++;
                    }
                }
                if (currentBatch == batchSize)
                {
                    skipSize += batchSize;
                    currentBatch = 0;
                }
                else
                {
                    break;
                }
            }
            await Task.WhenAll(tasks);
        }

        public static Job SubmitJob(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string transformName,
            string jobName, JobInput jobInput, string outputAssetName)
        {
            JobOutput[] jobOutputs =
            {
                new JobOutputAsset(outputAssetName),
            };
            //TODO : make sure the job name is unique
            Job job = client.Jobs.Create(resourceGroupName, accountName,  transformName, jobName,  
                new Job
                {
                    Input = jobInput,
                    Outputs = jobOutputs,
                });

            return job;
        }

        /* Media Services V2 */
        public static IAsset CreateAssetAndUploadSingleFile(Stream fileStream, string assetName, AssetCreationOptions options)
        {
            IAsset asset = MediaContext.Assets.Create(assetName, options);

            var assetFile = asset.AssetFiles.Create(assetName);
            assetFile.Upload(fileStream);

            return asset;
        }

        public static IAsset GetAsset(string assetName)
        {
            var result = MediaContext.Assets.Where(s => s.Name == assetName).ToList();
            if ((result != null) && (result.Count > 0))
            {
                return result[0];
            }
            return null;            
        }

        public static Task RunSubclipping(IAsset asset, string assetName, int priority, string configurationSubclip)
        {
            IJob job = MediaContext.Jobs.Create("Job for Live Analytics " + assetName, priority);
            IMediaProcessor processor = GetLatestMediaProcessorByName("Media Encoder Standard");            
            ITask task = job.Tasks.AddNew("Subclipping job task " + assetName, processor, configurationSubclip, TaskOptions.None);
            task.InputAssets.Add(asset);
            task.OutputAssets.AddNew(assetName, AssetCreationOptions.None);
            task.TaskNotificationSubscriptions.AddNew(NotificationJobState.FinalStatesOnly, NotificationEndPoint, true);
            return job.SubmitAsync();           
        }

        public static Task RunSubclippingWithRedactor(IAsset asset, string fileName, string outputAssetSubclip, string outputAssetLiveRedactor,
            int priority, string configurationSubclip, string configurationRedactor)
        {
            IJob job = MediaContext.Jobs.Create("Live Stream Analytics for " + fileName, priority);
            IMediaProcessor encoderProcessor = GetLatestMediaProcessorByName("Media Encoder Standard");
            ITask taskEncoder = job.Tasks.AddNew("Subclipping job task " + outputAssetSubclip, encoderProcessor, configurationSubclip, TaskOptions.None);
            taskEncoder.InputAssets.Add(asset);
            IAsset subclipAsset = taskEncoder.OutputAssets.AddNew(outputAssetSubclip, AssetCreationOptions.None);
                        
            var redactorProcessor = GetLatestMediaProcessorByName("Azure Media Redactor");            
            ITask taskRedactor = job.Tasks.AddNew("Live face redaction job task" + outputAssetLiveRedactor, redactorProcessor, configurationRedactor, TaskOptions.None);
            taskRedactor.InputAssets.Add(subclipAsset);
            taskRedactor.OutputAssets.AddNew(outputAssetLiveRedactor, AssetCreationOptions.None);
            taskRedactor.TaskNotificationSubscriptions.AddNew(NotificationJobState.FinalStatesOnly, NotificationEndPoint, true);
            taskRedactor.Priority = priority - 1;
            return job.SubmitAsync();            
        }

        public static Task EncodeToAdaptiveBitrateMP4Set(IAsset asset, string assetName, string preset)
        {                       
            IJob job = MediaContext.Jobs.Create("Media Encoder Standard Job " + assetName);            
            IMediaProcessor processor = GetLatestMediaProcessorByName("Media Encoder Standard");
            ITask task = job.Tasks.AddNew("Encoding job task " + assetName, processor, preset, TaskOptions.None);            
            task.InputAssets.Add(asset);          
            task.OutputAssets.AddNew(assetName, AssetCreationOptions.None);
            task.TaskNotificationSubscriptions.AddNew(NotificationJobState.FinalStatesOnly, NotificationEndPoint, true);
            return job.SubmitAsync();             
        }

        public static Task RunFaceRedactionJob(IAsset asset, string assetName, string configurationFile, TraceWriter log)
        {            
            IJob job = MediaContext.Jobs.Create("Face redaction job for " + assetName);            
            string MediaProcessorName = "Azure Media Redactor";
            var processor = GetLatestMediaProcessorByName(MediaProcessorName);            
            string configuration = File.ReadAllText(configurationFile);            
            ITask task = job.Tasks.AddNew("Face redaction task job " + assetName, processor, configuration, TaskOptions.None);
            task.InputAssets.Add(asset);            
            task.OutputAssets.AddNew(assetName, AssetCreationOptions.None);
            task.TaskNotificationSubscriptions.AddNew(NotificationJobState.FinalStatesOnly, NotificationEndPoint, true);
            return job.SubmitAsync();
        }

        public static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
        {
            var processor = MediaContext.MediaProcessors
            .Where(p => p.Name == mediaProcessorName)
            .ToList()
            .OrderBy(p => new Version(p.Version))
            .LastOrDefault();

            if (processor == null)
                throw new ArgumentException(string.Format("Unknown media processor",
                                       mediaProcessorName));

            return processor;
        }

        public static HttpClient GetHttpClientForVideo(TraceWriter log)
        {
            Tuple<HttpClient, PolicyWrap<HttpResponseMessage>> tuple = HttpClientList.GetOrAdd("video", new Tuple<HttpClient, PolicyWrap<HttpResponseMessage>>(
                        new HttpClient(),
                        FaceHelper.DefineAndRetrieveResiliencyStrategy(log)));
            return tuple.Item1;
        }

        public static async Task<bool> RunDetect(Guid requestID, string apis, string name, string source,
            Stream incomingPicture, string sourceContainerName, string resultContainerName, IAsyncCollector<object> outputItem, TraceWriter log, string videoName = null)
        {
            string apikey = string.Empty;
            try
            {
                string[] apiArr = apis.Split(',');
                int randomApi = FaceHelper.Instance.Next(0, apiArr.Length);
                apikey = apiArr[randomApi];
                log.Info($"RunDetect request id: {requestID} apiKey: {apikey} ticks: {DateTime.Now.Ticks}");
                Tuple<HttpClient, PolicyWrap<HttpResponseMessage>> tuple = FaceHelper.HttpClientList.GetOrAdd(apikey, new Tuple<HttpClient, PolicyWrap<HttpResponseMessage>>(
                    new HttpClient(),
                    FaceHelper.DefineAndRetrieveResiliencyStrategy(log)));
                HttpClient client = tuple.Item1;
                PolicyWrap<HttpResponseMessage> policy = tuple.Item2;
                IDatabase cache = FaceHelper.Connection.GetDatabase(int.Parse(FaceHelper.Connection.GetDatabase(1).StringGet(apikey)));                                  
                
                //the large group id it's based on the mac address we get - each MAC address can work with different face api group
                string largegroupid = ConfigurationManager.AppSettings[source];
                if (videoName == null)
                {
                    log.Info($"Detecting {name} requestId: {requestID} apiKey: {apikey} ticks: {DateTime.Now.Ticks}");
                }
                else
                {
                    log.Info($"Detecting thumbnail {name} from {videoName} requestId: {requestID} apiKey: {apikey} ticks: {DateTime.Now.Ticks}");
                }
                byte[] pictureImage;                
                // Convert the incoming image stream to a byte array.
                using (var br = new BinaryReader(incomingPicture))
                {
                    pictureImage = br.ReadBytes((int)incomingPicture.Length);
                }
                var detectionResult = await new FaceDetect(log, client).DetectFaces(pictureImage, apikey, requestID, policy);
                if ((detectionResult != null) && (detectionResult.Length > 0))
                {
                    //prepare identify request
                    int maxCandidate = int.Parse(ConfigurationManager.AppSettings["maxNumOfCandidatesReturned"]);
                    double threshold = double.Parse(ConfigurationManager.AppSettings["confidenceThreshold"]);
                    var identifyResquest = new FaceIdentifyRequest()
                    {
                        ConfidenceThreshold = threshold,
                        MaxNumOfCandidatesReturned = maxCandidate,
                        LargePersonGroupId = largegroupid,
                        FaceIds = detectionResult.Select(s => s.FaceId).ToArray()
                    };
                    var identifyResult = await new FaceIdentify(log, client).IdentifyFaces(identifyResquest, apikey, requestID, policy);
                    if ((identifyResult == null) || (identifyResult.Length == 0))
                    {
                        log.Info($"No identification result requestId: {requestID}, apiKey:{apikey} ticks: {DateTime.Now.Ticks}");                        
                    }                    
                    var personResult = new PersonDetails(log, client);
                    //merging results and find person name
                    for (int i = 0; i < detectionResult.Length; i++)
                    {
                        if (videoName == null)
                        {
                            detectionResult[i].FaceBlobName = string.Concat(detectionResult[i].FaceId, "_", name);
                        }
                        else
                        {
                            detectionResult[i].FaceBlobName = videoName + "/" + name;
                        }
                        if ((identifyResult != null) && (identifyResult.Length > 0))
                        {
                            detectionResult[i].Candidates = identifyResult[i].Candidates;
                            for (int j = 0; j < detectionResult[i].Candidates.Length; j++)
                            {
                                string personid = detectionResult[i].Candidates[j].PersonId.ToString();
                                string personName = cache.StringGet(largegroupid + "-" + personid);
                                if (string.IsNullOrEmpty(personName) == true)
                                {
                                    log.Info($"Missing cache requestId: {requestID} apiKey: {apikey} personId: {personid} ticks: {DateTime.Now.Ticks}");
                                    var tPerson = await personResult.GetPersonName(personid, apikey, largegroupid, requestID, policy);
                                    personName = tPerson.Name;
                                    cache.StringSet(largegroupid + "-" + personid, personName, null, When.Always);
                                }
                                detectionResult[i].Candidates[j].PersonName = new InternalPersonDetails()
                                {
                                    PersonId = detectionResult[i].Candidates[j].PersonId,
                                    Name = personName
                                };
                            }
                        }
                    }                                      
                }
                else
                {
                    log.Info($"No dectection result requestId: {requestID}, apiKey:{apikey} ticks: {DateTime.Now.Ticks}");
                    //in case of video, we want to create a link to the face detected by AMS (Azure Media Services) although face api didn't recognize it
                    if (videoName != null)
                        detectionResult = new FaceDetectResult[1] { new FaceDetectResult() { FaceBlobName = videoName + "/" + name } };
                }
                string blobname = videoName ?? name;
                var actionResult = new FaceIdentifyResult()
                {
                    BlobName = blobname,
                    ContainerName = sourceContainerName,
                    ResultContainerName = resultContainerName,
                    BlobLength = incomingPicture.Length,
                    CreatedDateTime = DateTime.UtcNow,
                    RequestId = requestID,
                    ApiKey = apikey,
                    LargeGroupId = largegroupid,
                    Source = source,
                    DetectResultList = detectionResult
                };
                string strResult = JsonConvert.SerializeObject(actionResult);
                await outputItem.AddAsync(strResult);
            }
            catch (Exception ex)
            {
                log.Error($"Exception Message: {ex.Message}, requestId: {requestID}, apiKey:{apikey} ticks: {DateTime.Now.Ticks}", ex);                
                return false;
            }
            return true;
        }

        public static Bitmap CropAtRect(Bitmap b, System.Drawing.Rectangle r)
        {
            Bitmap nb = new Bitmap(r.Width, r.Height);
            Graphics g = Graphics.FromImage(nb);
            g.DrawImage(b, -r.X, -r.Y);
            return nb;
        }

        public static Stream ToStream(System.Drawing.Image image, System.Drawing.Imaging.ImageFormat format)
        {
            var stream = new MemoryStream();
            image.Save(stream, format);
            stream.Position = 0;
            return stream;
        }

        public static ByteArrayContent GetIdentifyHttpContent(FaceIdentifyRequest request)
        {
            string strRequest = JsonConvert.SerializeObject(request);
            byte[] byteData = Encoding.UTF8.GetBytes(strRequest);

            var content = new ByteArrayContent(byteData);

            // Add application/octet-stream header for the content.
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            return content;
        }

        /// <summary>
        /// Request the ByteArrayContent object through a static method so
        /// it is not disposed when the Polly resiliency policy asynchronously
        /// executes our method that posts the image content to the Face API. 
        /// Otherwise, we'll receive the following error when the
        /// API service is throttled:
        /// System.ObjectDisposedException: Cannot access a disposed object. Object name: 'System.Net.Http.ByteArrayContent'
        /// 
        /// More information can be found on the HttpClient class in the
        /// .NET Core library source code:
        /// https://github.com/dotnet/corefx/blob/6d7fca5aecc135b97aeb3f78938a6afee55b1b5d/src/System.Net.Http/src/System/Net/Http/HttpClient.cs#L500
        /// </summary>
        /// <param name="imageBytes"></param>
        /// <returns></returns>
        public static ByteArrayContent GetImageHttpContent(byte[] imageBytes)
        {
            var content = new ByteArrayContent(imageBytes);

            // Add application/octet-stream header for the content.
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            return content;
        }


        /// <summary>
        /// Creates a Polly-based resiliency strategy that does the following when communicating
        /// with the external (downstream) Face API service:
        /// 
        /// If requests to the service are being throttled, as indicated by 429 or 503 responses,
        /// wait and try again in a bit by exponentially backing off each time. This should give the service
        /// enough time to recover or allow enough time to pass that removes the throttling restriction.
        /// This is implemented through the WaitAndRetry policy named 'waitAndRetryPolicy'.
        /// 
        /// Alternately, if requests to the service result in an HttpResponseException, or a number of
        /// status codes worth retrying (such as 500, 502, or 504), break the circuit to block any more
        /// requests for the specified period of time, send a test request to see if the error is still
        /// occurring, then reset the circuit once successful.
        /// 
        /// These policies are executed through a PolicyWrap, which combines these into a resiliency
        /// strategy. For more information, see: https://github.com/App-vNext/Polly/wiki/PolicyWrap
        /// 
        /// NOTE: A longer-term resiliency strategy would have us share the circuit breaker state across
        /// instances, ensuring subsequent calls to the struggling downstream service from new instances
        /// adhere to the circuit state, allowing that service to recover. This could possibly be handled
        /// by a Distributed Circuit Breaker (https://github.com/App-vNext/Polly/issues/287) in the future,
        /// or perhaps by using Durable Functions that can hold the state.
        /// </summary>
        /// <returns></returns>
        public static PolicyWrap<HttpResponseMessage> DefineAndRetrieveResiliencyStrategy(TraceWriter _log)
        {
            
            _log.Info($"Defining new resiliency strategy");
            // Retry when these status codes are encountered.
            HttpStatusCode[] httpStatusCodesWorthRetrying = {
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.GatewayTimeout // 504
            };

            // Immediately fail (fail fast) when these status codes are encountered.
            HttpStatusCode[] httpStatusCodesToImmediatelyFail = {
                HttpStatusCode.BadRequest, // 400
                HttpStatusCode.Unauthorized, // 401
                HttpStatusCode.Forbidden // 403
            };

            // Define our waitAndRetry policy: retry n times with an exponential backoff in case the Face API throttles us for too many requests.
            var waitAndRetryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(e => e.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    e.StatusCode == (System.Net.HttpStatusCode)429 || e.StatusCode == (System.Net.HttpStatusCode)403)
                .WaitAndRetryAsync(10, // Retry 10 times with a delay between retries before ultimately giving up
                    attempt => TimeSpan.FromSeconds(0.25 * Math.Pow(2, attempt)), // Back off!  2, 4, 8, 16 etc times 1/4-second
                                                                                    //attempt => TimeSpan.FromSeconds(6), // Wait 6 seconds between retries
                    (exception, calculatedWaitDuration) =>
                    {
                        _log.Info($"Face API server is throttling our requests. Automatically delaying for {calculatedWaitDuration.TotalMilliseconds}ms");
                    }
                );

            // Define our first CircuitBreaker policy: Break if the action fails 4 times in a row.
            // This is designed to handle Exceptions from the Face API, as well as
            // a number of recoverable status messages, such as 500, 502, and 504.
            var circuitBreakerPolicyForRecoverable = Policy
                .Handle<HttpResponseException>()
                .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 3,
                    durationOfBreak: TimeSpan.FromSeconds(3),
                    onBreak: (outcome, breakDelay) =>
                    {
                        _log.Info($"Polly Circuit Breaker logging: Breaking the circuit for {breakDelay.TotalMilliseconds}ms due to: {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
                    },
                    onReset: () => _log.Info("Polly Circuit Breaker logging: Call ok... closed the circuit again"),
                    onHalfOpen: () => _log.Info("Polly Circuit Breaker logging: Half-open: Next call is a trial")
                );

            // Combine the waitAndRetryPolicy and circuit breaker policy into a PolicyWrap. This defines our resiliency strategy.
            return Policy.WrapAsync(waitAndRetryPolicy, circuitBreakerPolicyForRecoverable);
            
        }
        public static EndTimeInTable RetrieveLastEndTime(CloudTable table, string programID)
        {
            TableOperation tableOperation = TableOperation.Retrieve<EndTimeInTable>(programID, "lastEndTime");
            TableResult tableResult = table.Execute(tableOperation);
            return tableResult.Result as EndTimeInTable;
        }

        public static void UpdateLastEndTime(CloudTable table, TimeSpan endtime, string programId, int id, ProgramState state)
        {
            var endTimeInTableEntity = new EndTimeInTable();
            endTimeInTableEntity.ProgramId = programId;
            endTimeInTableEntity.Id = id.ToString();
            endTimeInTableEntity.ProgramState = state.ToString();
            endTimeInTableEntity.LastEndTime = endtime.ToString();
            endTimeInTableEntity.AssignPartitionKey();
            endTimeInTableEntity.AssignRowKey();
            TableOperation tableOperation = TableOperation.InsertOrReplace(endTimeInTableEntity);
            table.Execute(tableOperation);
        }
        public static string OutputStorageFromParam(dynamic objParam)
        {
            return (objParam != null) ? (string)objParam.outputStorage : null;
        }

        public static TimeSpan ReturnTimeSpanOnGOP(ManifestTimingData data, TimeSpan ts)
        {
            var retVal = ts;
            ulong timestamp = (ulong)(ts.TotalSeconds * data.TimeScale);
            int i = 0;
            foreach (var t in data.TimestampList)
            {
                if (t < timestamp && i < (data.TimestampList.Count - 1) && timestamp < data.TimestampList[i + 1])
                {
                    retVal = TimeSpan.FromSeconds((double)t / (double)data.TimeScale);
                    break;
                }
                i++;
            }
            return retVal;
        }

        public static ILocator CreatedTemporaryOnDemandLocator(IAsset asset)
        {
            ILocator tempLocator = null;
            try
            {
                var locatorTask = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        tempLocator = asset.GetMediaContext().Locators.Create(LocatorType.OnDemandOrigin, asset, AccessPermissions.Read, TimeSpan.FromHours(1));
                    }
                    catch
                    {
                        throw;
                    }
                });
                locatorTask.Wait();
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return tempLocator;
        }

        public static ManifestTimingData GetManifestTimingData(IAsset asset, TraceWriter log)
        {
            ManifestTimingData response = new ManifestTimingData() { IsLive = false, Error = false, TimestampOffset = 0, TimestampList = new List<ulong>() };
            try
            {
                ILocator mytemplocator = null;
                Uri myuri = FaceHelper.GetValidOnDemandURI(asset);
                if (myuri == null)
                {
                    mytemplocator = CreatedTemporaryOnDemandLocator(asset);
                    myuri = FaceHelper.GetValidOnDemandURI(asset);
                }
                if (myuri != null)
                {
                    XDocument manifest = XDocument.Load(myuri.ToString());
                    var smoothmedia = manifest.Element("SmoothStreamingMedia");
                    var videotrack = smoothmedia.Elements("StreamIndex").Where(a => a.Attribute("Type").Value == "video");
                    string timescalefrommanifest = smoothmedia.Attribute("TimeScale").Value;
                    if (videotrack.FirstOrDefault().Attribute("TimeScale") != null) // there is timescale value in the video track. Let's take this one.
                    {
                        timescalefrommanifest = videotrack.FirstOrDefault().Attribute("TimeScale").Value;
                    }
                    ulong timescale = ulong.Parse(timescalefrommanifest);
                    response.TimeScale = (ulong?)timescale;
                    if (videotrack.FirstOrDefault().Element("c").Attribute("t") != null)
                    {
                        response.TimestampOffset = ulong.Parse(videotrack.FirstOrDefault().Element("c").Attribute("t").Value);
                    }
                    else
                    {
                        response.TimestampOffset = 0; // no timestamp, so it should be 0
                    }

                    ulong totalduration = 0;
                    ulong durationpreviouschunk = 0;
                    ulong durationchunk;
                    int repeatchunk;
                    foreach (var chunk in videotrack.Elements("c"))
                    {
                        durationchunk = chunk.Attribute("d") != null ? ulong.Parse(chunk.Attribute("d").Value) : 0;
                        repeatchunk = chunk.Attribute("r") != null ? int.Parse(chunk.Attribute("r").Value) : 1;
                        totalduration += durationchunk * (ulong)repeatchunk;
                        if (chunk.Attribute("t") != null)
                        {
                            //totalduration = ulong.Parse(chunk.Attribute("t").Value) - response.TimestampOffset; // new timestamp, perhaps gap in live stream....
                            response.TimestampList.Add(ulong.Parse(chunk.Attribute("t").Value));
                        }
                        else
                        {
                            response.TimestampList.Add(response.TimestampList[response.TimestampList.Count() - 1] + durationpreviouschunk);
                        }

                        for (int i = 1; i < repeatchunk; i++)
                        {
                            response.TimestampList.Add(response.TimestampList[response.TimestampList.Count() - 1] + durationchunk);
                        }

                        durationpreviouschunk = durationchunk;
                    }
                    response.TimestampEndLastChunk = response.TimestampList[response.TimestampList.Count() - 1] + durationpreviouschunk;
                    if (smoothmedia.Attribute("IsLive") != null && smoothmedia.Attribute("IsLive").Value == "TRUE")
                    { // Live asset.... No duration to read (but we can read scaling and compute duration if no gap)
                        response.IsLive = true;
                        response.AssetDuration = TimeSpan.FromSeconds((double)totalduration / ((double)timescale));
                    }
                    else
                    {
                        totalduration = ulong.Parse(smoothmedia.Attribute("Duration").Value);
                        response.AssetDuration = TimeSpan.FromSeconds((double)totalduration / ((double)timescale));
                    }
                }
                else
                {
                    response.Error = true;
                }
                if (mytemplocator != null) mytemplocator.Delete();
            }
            catch (Exception)
            {
                response.Error = true;
            }
            return response;
        }
    }
}
