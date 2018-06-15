# Serverless Face Detection using Azure
Sample code for implementing serverless face detection solution for images, videos and live stream using Azure

<B>The Motivation</B><br>
The main motivation for building this solution is to show how you can integrate between different Azure services to come with one scalable solution that can offer new capabilities to our end users.

<B>The Idea</B><br>
This solution show how you can use Azure Face API with <a href="https://docs.microsoft.com/en-us/azure/cognitive-services/face/overview">Azure Media Services (AMS)</a> to perform face identification on uploaded images, recorded videos and live stream channels. <br>
We are going to solve the problem by splitting it into small operations (Azure Functions) that react to events (Azure Event Grid). 
The basic operation flow is to react to a new image upload event, detect the faces and then perform face identification for each of detected faces using Face API large group capabilities.<br>
This task will be triggered by all scenarios (image, video, stream), the difference is just the way we are going to extract the images that will be the input for this operation. <br>
In order to solve the recorded video scenario, we are going to use AMS – Redactor capability that provide the ability to extract faces thumbnails (analyze mode) which being recognized during the video, each face thumbnail is the input for the basic operation. <br>
For live stream scenario, we are going to use the AMS – Live Streaming and Azure Logic App which going to call an Azure Function for sub-clipping the live channel. The Logic App will trigger every 10sec, once the sub-clipping task is completed, it will start the process of redactor operation which going to produce face thumbnails that appear in the sub-clipped video, once completed our basic operation will be triggered. <br><br>
Other Azure services that we are using in this solution are: Azure Storage, Azure Redis Cache, Azure Cosmos DB, Azure App Service(*out of scope of this post) and Azure Application Insights. <br><br>
The Azure Functions deployment will be performed by Visual Studio Team Services (VSTS) 
