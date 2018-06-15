# Serverless Face Detection using Azure
**** This document is still in progress ****<br>
Sample code for implementing serverless face detection solution for images, videos and live stream using Azure

<B>The Motivation</B><br>
The main motivation for building this solution is to show how you can integrate between different Azure services to come with one scalable solution that can offer new capabilities to our end users.

<B>The Idea</B><br>
This solution show how you can use <a href="https://docs.microsoft.com/en-us/azure/cognitive-services/face/overview">Azure Face API</a> with <a href="https://docs.microsoft.com/en-us/azure/cognitive-services/face/overview">Azure Media Services (AMS)</a> to perform face identification on uploaded images, recorded videos and live stream channels. <br>
We are going to solve the problem by splitting it into small operations (<a href="https://azure.microsoft.com/en-us/services/functions/">Azure Functions</a>) that react to events (<a href="https://azure.microsoft.com/en-us/services/event-grid/">Azure Event Grid</a>). 
The basic operation flow is to react to a new image upload event, detect the faces and then perform face identification for each of detected faces using <a href="https://docs.microsoft.com/en-us/azure/cognitive-services/face/face-api-how-to-topics/how-to-use-large-scale">Face API large group capabilities</a>.<br>
This task will be triggered by all scenarios (image, video, stream), the difference is just the way we are going to extract the images that will be the input for this operation. <br>
In order to solve the recorded video scenario, we are going to use <a href="https://docs.microsoft.com/en-us/azure/media-services/previous/media-services-face-redaction">AMS – Redactor capability</a> that provide the ability to extract faces thumbnails (analyze mode) which being recognized during the video, each face thumbnail is the input for the basic operation. <br>
For live stream scenario, we are going to use the <a href="https://docs.microsoft.com/en-us/azure/media-services/previous/media-services-manage-channels-overview">AMS – Live Streaming</a> and <a href="https://azure.microsoft.com/en-us/services/logic-apps/">Azure Logic App </a> which going to call an Azure Function for sub-clipping the live channel. The Logic App will trigger every 10sec, once the sub-clipping task is completed, it will start the process of redactor operation which going to produce face thumbnails that appear in the sub-clipped video, once completed our basic operation will be triggered. <br><br>
Other Azure services that we are using in this solution are: <a href="https://azure.microsoft.com/en-us/services/storage/">Azure Storage</a>, <a href="https://azure.microsoft.com/en-us/services/cache/">Azure Redis Cache</a>, <a href="https://docs.microsoft.com/en-us/azure/cosmos-db/introduction">Azure Cosmos DB</a>, <a href="https://azure.microsoft.com/en-us/services/app-service/">Azure App Service</a>(*out of scope) and <a href="https://azure.microsoft.com/en-us/services/application-insights/">Azure Application Insights</a>. <br><br>
The Azure Functions deployment will be performed by <a href="https://www.visualstudio.com/team-services/">Visual Studio Team Services (VSTS)</a><br>

<B>Solution Architecture</B><br>
![alt text](https://github.com/yaprigal/ServerlessFaceDetection/blob/master/Capture.PNG)
