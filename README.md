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
![Screen capture](https://github.com/yaprigal/ServerlessFaceDetection/blob/master/Capture.PNG?raw=true)
### 1. Prerequisites
1.	Azure account with the following services (on same region), if you don’t have create a <a href="https://azure.microsoft.com/en-us/free/">free Azure account</a>:<br>
a.	One Azure Face API instance with one large person group created and trained as explained <a href="https://docs.microsoft.com/en-us/azure/cognitive-services/face/face-api-how-to-topics/how-to-use-large-scale">here</a> , this group contain the face images to detect<br>
b. Create an Azure Media Services account and assign a service principal to it, keep the password. To do so, go to the API access tab in the account ([follow this article](https://docs.microsoft.com/en-us/azure/media-services/media-services-portal-get-started-with-aad#service-principal-authentication))<br>
c.	One Function App instance (consumption plan) with Application Insights associated to it<br>
d.	One Azure Cosmos DB instance with 3 collections (photo, video, stream) <br>
e.	One Event Grid Topic instance<br>
f.	One Azure Redis Cache instance<br>
g.	One instance of Azure Storage V2 (general purpose v2) with 6 containers (private access), for example:<br>
image, imageresult, video, videoresult, stream and streamresult<br>
2.	VSTS account <br>
If you don’t have a Visual Studio Team services account yet, <a href="https://go.microsoft.com/fwlink/?LinkId=307137">open one now</a> 
3. Fork this project to your github account
4.	Alternative for step 2 and 3, is to deploy the function app using Visual Studio 2017 with Azure Functions tools extension – see <a href="https://docs.microsoft.com/en-us/azure/azure-functions/functions-develop-vs">here</a> for more information 

### 2. Code Structure
 #### 2.1 source folder
      DetectionApp - Containes the Detection FuncApp source
      UploadedImages - Test project for testing image upload (simple / bulk)
      TestImage - The images that will be used by Test project
 #### 2.2 VSTS folder
      VSTS Function Build.json - contain build definition that need to imported to VSTS 
      VSTS Function Release.json - contain release definition that need to import to VSTS project

### 3. Setting Up VSTS Environment
In VSTS create a new project, once created, go to builds tab and choose to import – “VSTS Function Build.json”.
<br>Select “Hosted VS2017” as agent queue. <br>
Under task list, select “Get sources” – choose your forked GitHub project as the source control. 
Queue and Build – verify the build completed successfully.<br>
Import “VSTS Function Release.json” to new release definition – fix the agent queue to be “Hosted VS2017”.
<br>Select “Dev” environment – choose your Azure subscription, authorize it and choose the function app you created.<br>
Go to Variables tab – update the values according to below list, once completed start a new release deployment.<br>

#### 3.1 Function Application Settings
     "AMSAADTenantDomain": "YOUR_TENANT_DOMAIN.onmicrosoft.com"
     "AMSRESTAPIEndpoint": "YOUR_AMS_API_ENDPOINT"
     "AMSClientId": "YOUR_SERVICE_PRINCIPAL_CLIENT_ID"
     "AMSClientSecret": "YOUR_SERVICE_PRINCIPAL_CLIENT_SECRET"
     "AMSPreset": "H264 Single Bitrate 1080p"
     "apis": "YOUR_FACE_API_KEYS_SPLITTED_BY_COMMA"
     "confidenceThreshold": 0.5
     "copysubclip": "1_IF_YOU_WANT_TO_COPY_STREAM_SUBCLIP_VIDEOS_TO_STREAM_SOURCE_CONTAINER_OTHERWISE_0"
     "eventGridTopicEndpoint": "YOUR_EVENT_GRID_TOPIC_ENDPOINT"
     "eventGridTopicKey": "YOUR_EVENT_GRID_TOPIC_KEY"
     "faceDetectApiUrl": "https://YOUR_REGION.api.cognitive.microsoft.com/face/v1.0/detect"
     "faceIdentifyApiUrl": "https://YOUR_REGION.api.cognitive.microsoft.com/face/v1.0/identify"
     "facePersonApiUrl": "https://YOUR_REGION.api.cognitive.microsoft.com/face/v1.0/largepersongroups"
     "maxNumOfCandidatesReturned": 3
     "myamsconn": "YOUR_AMS_STORAGE_CONNECTION_STRING"
     "myblobconn": "YOUR_STORAGE_CONNECTION_STRING"
     "myCosmosDBConnection": "YOUR_COSMOSDB_CONNECTION_STRING"
     "myRedis": "YOUR_REDIS_CACHE_CONNECTION_STRING"
     "NotificationSigningKey": "RANDOM_BASE64_ENCODE_STRING"
     "NotificationWebHookEndpoint": "YOUR_FUNCTION_NOTIFICATION_WEBHOOK_ENDPOINT"
     "PREFIX_FILE_NAME": "YOUR_FACE_API_LARGE_GROUP_NAME"
     "resultcontainername": "YOUR_IMAGE_RESULT_CONTAINER_NAME"
     "sourcecontainername": "YOUR_IMAGE_SOURCE_CONTAINER_NAME"
     "streamresultcontainername": "YOUR_STREAM_RESULT_CONTAINER_NAME"
     "streamsourcecontainername": "YOUR_STREAM_SOURCE_CONTAINER_NAME"
     "videoresultcontainername": "YOUR_VIDEO_RESULT_CONTAINER_NAME"
     "videosourcecontainername": "YOUR_VIDEO_SOURCE_CONTAINER_NAME"
     * notes: 
     "PREFIX_FILE_NAME" – every image/video/live channel has a prefix which define the large group name. 
     e.g. image name: mycam-picture.jpg means you need to define a variable with the name: mycam and assigned it the value of face API large group you created.
     "NotificationWebHookEndpoint" – you will need to take this value after your first deployment – go to your deployed function app –> NotificationWebhook – get function URL
     "NotificationSigningKey " – generated base64 string
     “apis” - you can create more than one Face API as it might that the current limitation of Face API of 10 transactions per second not sufficient you. (although resiliency strategy implemented, you might experience longer function completion time in case of heavy load)
     
### 4. Define Event Grid  
#### 4.1 Storage events      
      
   Go to your deployed function app ->
   Select TriggerByImageUploadFunc - click on "Add Event Grid subscription" - give it a name, select 'Storage Account' as topic type, 
   select the relevant storage (from above 1.g section), uncheck 'Subscribe to all event types', check on 'Blob Created' and 
   add /blobServices/default/containers/images/blobs/ as Prefix Filter.
            
   Select TriggerByVideoUploadFunc - repeat previous step, this time just replace the prefix filter to
   /blobServices/default/containers/video/blobs/
      
   Select TriggerByVideoThumbnail - repeat previous step, this time just replace the prefix filter to
   /blobServices/default/containers/videoresult/blobs/
      
   Select TriggerByStreamThumnail - repeat previous step, this time just replace the prefix filter to
   /blobServices/default/containers/streamresult/blobs/
 
   All above assume your container names are images, video, videoresult and streamresult. (see section 1.g)
      
#### 4.2 Event Topic Subscription 
   Go to your deployed function app and get the function url of the following functions: EncodeProcessing, RedactorProcessing and
   CopyFaceProcessing.
     
   Go to your deployed Event Grid Topic to add a new Event Subscription.
     
   Create Event Subscription for EncodeProcessing
   Uncheck 'Subscribe to all event types', add Event Type 'encode', select Web Hook as Endpoint Type, put as endpoint the URL of
   EncodeProcessing function, give it a name as click on Create.
     
   Create Event Subscription for RedactorProcessing
   Uncheck 'Subscribe to all event types', add Event Type 'redactor', select Web Hook as Endpoint Type, put as endpoint the URL of
   RedactorProcessing function, give it a name as click on Create.h
     
   Create Event Subscription for RedactorProcessing
   Uncheck 'Subscribe to all event types', add Event Type 'copy', select Web Hook as Endpoint Type, put as endpoint the URL of
   CopyFaceProcessing function, give it a name as click on Create.

### 5. Load Azure Redis Cache
   Redis Cache contains person id and person name as the key-value store per Face API deployed.<br>
   To load the values into your deployed Redis Cache you should call the deployed “LoadPersonGroup” function when passing as ‘personid’ 
   the name of the person group that you created in your face API (step 1.a).<br>
   e.g. https://YOUR_FUNCTION_URL/LoadPersonGroup?groupid=YOUR_FACE_API_LARGE_GROUP_NAME
   
   Note: this operation will go over all your configured ‘apis’
### 6. Deploy Azure Logic App
   This template creates a Logic app which processes a live program (from a live channel in Azure Media Services) for media analytics.
   It's sub-clipping the last given interval and processing a 'redactor' analysis on the sub-clipping output.
   
   <a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fyaprigal%2FServerlessFaceDetection%2Fmaster%2FLogicApp.json" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

     
     
