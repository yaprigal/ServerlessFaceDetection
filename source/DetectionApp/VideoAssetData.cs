using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DetectionApp
{
    public class VideoAssetData
    {
        [JsonProperty(PropertyName = "requestId")]
        public string RequestId { get; set; }
        [JsonProperty(PropertyName = "assetName")]
        public string AssetName { get; set; }
        [JsonProperty(PropertyName = "jobName")]
        public string JobName { get; set; }
        [JsonProperty(PropertyName = "videoName")]
        public string VideoName { get; set; }
        [JsonProperty(PropertyName = "containerName")]
        public string ContainerName { get; set; }
        [JsonProperty(PropertyName = "timeStamp")]
        public DateTime TimeStamp { get; set; }
        [JsonProperty(PropertyName = "step")]
        public VideoAnalysisSteps Step { get; set; }
        [JsonProperty(PropertyName = "prevstep")]
        public VideoAnalysisSteps PrevStep { get; set; }
    }

    public enum VideoAnalysisSteps
    {
        Unknown,
        Upload,
        Encode,
        Redactor,
        Copy,       
        LiveRedactor,
        Subclip
    }
}