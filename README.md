# Serverless Face Detection using Azure
**** This document is still in progress ****<br>
Sample code for implementing serverless face detection solution for images, videos and live stream using Azure

## The Motivation
The main motivation for building this solution is to show how you can integrate between different Azure services to come with one scalable solution that can offer new capabilities to our end users.

## The Idea
This solution show how you can use <a href="https://docs.microsoft.com/en-us/azure/cognitive-services/face/overview">Azure Face API</a> with <a href="https://docs.microsoft.com/en-us/azure/cognitive-services/face/overview">Azure Media Services (AMS)</a> to perform face identification on uploaded images, recorded videos and live stream channels. <br>
We are going to solve the problem by splitting it into small operations (<a href="https://azure.microsoft.com/en-us/services/functions/">Azure Functions</a>) that react to events (<a href="https://azure.microsoft.com/en-us/services/event-grid/">Azure Event Grid</a>). 
The basic operation flow is to react to a new image upload event, detect the faces and then perform face identification for each of detected faces using <a href="https://docs.microsoft.com/en-us/azure/cognitive-services/face/face-api-how-to-topics/how-to-use-large-scale">Face API large group capabilities</a>.<br>
This task will be triggered by all scenarios (image, video, stream), the difference is just the way we are going to extract the images that will be the input for this operation. <br>
In order to solve the recorded video scenario, we are going to use <a href="https://docs.microsoft.com/en-us/azure/media-services/previous/media-services-face-redaction">AMS – Redactor capability</a> that provide the ability to extract faces thumbnails (analyze mode) which being recognized during the video, each face thumbnail is the input for the basic operation. <br>
For live stream scenario, we are going to use the <a href="https://docs.microsoft.com/en-us/azure/media-services/previous/media-services-manage-channels-overview">AMS – Live Streaming</a> and <a href="https://azure.microsoft.com/en-us/services/logic-apps/">Azure Logic App </a> which going to call an Azure Function for sub-clipping the live channel. The Logic App will trigger every 10sec, once the sub-clipping task is completed, it will start the process of redactor operation which going to produce face thumbnails that appear in the sub-clipped video, once completed our basic operation will be triggered. <br><br>
Other Azure services that we are using in this solution are: <a href="https://azure.microsoft.com/en-us/services/storage/">Azure Storage</a>, <a href="https://azure.microsoft.com/en-us/services/cache/">Azure Redis Cache</a>, <a href="https://docs.microsoft.com/en-us/azure/cosmos-db/introduction">Azure Cosmos DB</a>, <a href="https://azure.microsoft.com/en-us/services/app-service/">Azure App Service</a>(*out of scope) and <a href="https://azure.microsoft.com/en-us/services/application-insights/">Azure Application Insights</a>. <br><br>
The Azure Functions deployment will be performed by <a href="https://www.visualstudio.com/team-services/">Visual Studio Team Services (VSTS)</a><br>

## Solution Architecture
![alt text](https://github.com/yaprigal/ServerlessFaceDetection/blob/master/Capture.PNG)

### 1. Prerequisites
1.	Azure account with the following services (on same region), if you don’t have create a <a href="https://azure.microsoft.com/en-us/free/">free Azure account</a>:<br>
a.	One Azure Media Services instance with one large person group created and trained as explained <a href="https://docs.microsoft.com/en-us/azure/cognitive-services/face/face-api-how-to-topics/how-to-use-large-scale">here</a> , this group contain the face images to detect<br>
b.	One Function App instance (consumption plan) with Application Insights associated to it<br>
c.	One Azure Cosmos DB instance with 3 collections (photo, video, stream) <br>
d.	One Event Grid Topic instance<br>
e.	One Azure Redis Cache instance<br>
f.	One instance of Azure Storage V2 (general purpose v2) with 6 containers, for example:<br>
image, imageresult, video, videoresult, stream and streamresult<br>
2.	VSTS account <br>
If you don’t have a Visual Studio Team services account yet, <a href="https://go.microsoft.com/fwlink/?LinkId=307137">open one now</a> 
<br>
<b>Code Structure</b><br>
 source folder – contain the sample code projects<br>
 vsts folder – contain build and release definition which can be import to VSTS project<br>

### 2. Function Application Settings
"AMSAADTenantDomain": "YOUR_TENANT_DOMAIN.onmicrosoft.com"<br>
"AMSRESTAPIEndpoint": "YOUR_AMS_API_ENDPOINT"<br>
"AMSClientId": "YOUR_SERVICE_PRINCIPAL_CLIENT_ID"<br>
"AMSClientSecret": "YOUR_SERVICE_PRINCIPAL_CLIENT_SECRET"<br>
"AMSPreset": "H264 Single Bitrate 1080p"<br>
"apis": "YOUR_FACE_API_KEYS_SPLITTED_BY_COMMA"<br>
"confidenceThreshold": 0.5<br>
"copysubclip": "1_IF_YOU_WANT_TO_COPY_STREAM_SUBCLIP_VIDEOS_TO_STREAM_SOURCE_CONTAINER_OTHERWISE_0"<br>
"eventGridTopicEndpoint": "YOUR_EVENT_GRID_TOPIC_ENDPOINT"<br>
"eventGridTopicKey": "YOUR_EVENT_GRID_TOPIC_KEY"<br>
"faceDetectApiUrl": "https://YOUR_REGION.api.cognitive.microsoft.com/face/v1.0/detect"<br>
"faceIdentifyApiUrl": "https://YOUR_REGION.api.cognitive.microsoft.com/face/v1.0/identify"<br>
"facePersonApiUrl": "https://YOUR_REGION.api.cognitive.microsoft.com/face/v1.0/largepersongroups"<br> 
"maxNumOfCandidatesReturned": 3<br>
"myamsconn": "YOUR_AMS_STORAGE_CONNECTION_STRING"<br>
"myblobconn": "YOUR_STORAGE_CONNECTION_STRING"<br>
"myCosmosDBConnection": "YOUR_COSMOSDB_CONNECTION_STRING"<br>
"myRedis": "YOUR_REDIS_CACHE_CONNECTION_STRING"<br>
"NotificationSigningKey": "RANDOM_BASE64_ENCODE_STRING"<br>
"NotificationWebHookEndpoint": "YOUR_FUNCTION_NOTIFICATION_WEBHOOK_ENDPOINT"<br>
"PREFIX_FILE_NAME": "YOUR_FACE_API_LARGE_GROUP_NAME"<br>
"resultcontainername": "YOUR_IMAGE_RESULT_CONTAINER_NAME"<br>
"sourcecontainername": "YOUR_IMAGE_SOURCE_CONTAINER_NAME"<br>
"streamresultcontainername": "YOUR_STREAM_RESULT_CONTAINER_NAME"<br>
"streamsourcecontainername": "YOUR_STREAM_SOURCE_CONTAINER_NAME"<br>
"videoresultcontainername": "YOUR_VIDEO_RESULT_CONTAINER_NAME"<br>
"videosourcecontainername": "YOUR_VIDEO_SOURCE_CONTAINER_NAME"<br>
<br>


