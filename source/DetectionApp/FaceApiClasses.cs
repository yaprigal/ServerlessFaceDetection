using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SimpleFaceDetect
{
    public class FaceIdentifyResult
    {
        [JsonProperty(PropertyName = "requestID")]
        public Guid RequestId { get; set; }
        [JsonProperty(PropertyName = "apiKey")]
        public string ApiKey { get; set; }
        [JsonProperty(PropertyName = "blobName")]
        public string BlobName { get; set; }
        [JsonProperty(PropertyName = "containerName")]
        public string ContainerName { get; set; }
        [JsonProperty(PropertyName = "resultContainerName")]
        public string ResultContainerName { get; set; }
        [JsonProperty(PropertyName = "blobLength")]
        public long BlobLength { get; set; }
        [JsonProperty(PropertyName = "createdDateTime")]
        public DateTime CreatedDateTime { get; set; }
        [JsonProperty(PropertyName = "source")]
        public string Source { get; set; }
        [JsonProperty(PropertyName = "largeGroupId")]
        public string LargeGroupId { get; set; }
        [JsonProperty(PropertyName = "detectResultList")]
        public FaceDetectResult[] DetectResultList { get; set;}
    }


    public class FaceDetectResult
    {
        [JsonProperty(PropertyName = "faceId")]
        public Guid FaceId { get; set; }
        [JsonProperty(PropertyName = "faceRectangle")]
        public InternalFaceRectangle FaceRectangle { get; set; }
        [JsonProperty(PropertyName = "faceBlobName")]
        public string FaceBlobName { get; set; }
        [JsonProperty(PropertyName = "candidates")]
        public InternalFaceCandidate[] Candidates { get; set; }
    }

    public class InternalFaceCandidate
    {
        [JsonProperty(PropertyName = "personId")]
        public Guid PersonId { get; set; }
        [JsonProperty(PropertyName = "confidence")]
        public double Confidence { get; set; }
        [JsonProperty(PropertyName = "personName")]
        public InternalPersonDetails PersonName { get; set; }
    }

    public class InternalPersonDetails
    {
        [JsonProperty(PropertyName = "personId")]
        public Guid PersonId { get; set; }
        [JsonProperty(PropertyName = "persistedFaceIds")]
        public Guid[] PersistedFaceIds { get; set; }
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }
        [JsonProperty(PropertyName = "userData")]
        public string UserData { get; set; }
    }

    public class InternalFaceRectangle
    {
        [JsonProperty(PropertyName = "width")]
        public int Width { get; set; }
        [JsonProperty(PropertyName = "height")]
        public int Height { get; set; }
        [JsonProperty(PropertyName = "left")]
        public int Left { get; set; }
        [JsonProperty(PropertyName = "top")]
        public int Top { get; set; }
    }

    public class FaceIdentifyRequest
    {
        [JsonProperty(PropertyName = "largePersonGroupId")]
        public string LargePersonGroupId { get; set; }

        [JsonProperty(PropertyName = "faceIds")]
        public Guid[] FaceIds { get; set; }

        [JsonProperty(PropertyName = "maxNumOfCandidatesReturned")]
        public int MaxNumOfCandidatesReturned { get; set; }

        [JsonProperty(PropertyName = "confidenceThreshold")]
        public double ConfidenceThreshold { get; set; }
    }
}
