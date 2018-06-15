using Microsoft.Azure.Documents;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using SimpleFaceDetect;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DetectionApp
{
    public static class SaveResultImage
    {        
        [FunctionName("SaveResultImage")]
        public static async Task Run([CosmosDBTrigger(
            databaseName: "YOUR_COSMOS_DB_NAME",
            collectionName: "YOUR_PHOTO_COLLECTION_NAME",
            ConnectionStringSetting = "myCosmosDBConnection",
            LeaseCollectionName = "leases", CreateLeaseCollectionIfNotExists= true)]IReadOnlyList<Document> documents,            
            TraceWriter log)
        {
            Guid requestID = Guid.NewGuid();
            string strException = string.Empty;
            log.Info($"Processing SaveResultImage request id: {requestID} document count: {documents.Count} ticks: {DateTime.Now.Ticks}");
            try
            {
                BlobManager blobManager = new BlobManager(ConfigurationManager.AppSettings["myblobconn"]);
                if (documents != null && documents.Count > 0)
                {
                    for(int i = 0; i < documents.Count; i++)
                    {                                                
                        var doc = JsonConvert.DeserializeObject<FaceIdentifyResult>(documents[i].ToString());
                        if (doc.DetectResultList.Length > 0)
                        {
                            using (MemoryStream stream = await blobManager.DownloadAsync(doc.ContainerName, doc.BlobName))
                            {
                                for (int j = 0; j < doc.DetectResultList.Length; j++)
                                {
                                    log.Info($"Save new face candidate {requestID} face id: {doc.DetectResultList[j].FaceId} ticks: {DateTime.Now.Ticks}");
                                    string resultBlobName = doc.DetectResultList[j].FaceBlobName;
                                    //TODO : find better way to calc the x,y
                                    int x = doc.DetectResultList[j].FaceRectangle.Left - 100;
                                    int y = doc.DetectResultList[j].FaceRectangle.Top - 100;
                                    int width = (int)Math.Round(doc.DetectResultList[j].FaceRectangle.Width * 2.5);
                                    int height = (int)Math.Round(doc.DetectResultList[j].FaceRectangle.Height * 2.5);

                                    Rectangle sourceRectangle = new Rectangle(x, y, width, height);
                                    Rectangle destinationRectangle = new Rectangle(0, 0, width, height);

                                    Bitmap croppedImage = new Bitmap(destinationRectangle.Width, destinationRectangle.Height, PixelFormat.Format24bppRgb);
                                    using (Bitmap src = Image.FromStream(stream) as Bitmap)
                                    {
                                        croppedImage.SetResolution(src.HorizontalResolution, src.VerticalResolution);
                                        using (Graphics g = Graphics.FromImage(croppedImage))
                                        {
                                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                            g.CompositingQuality = CompositingQuality.HighQuality;
                                            g.SmoothingMode = SmoothingMode.HighQuality;
                                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                            g.DrawImage(src, destinationRectangle, sourceRectangle, GraphicsUnit.Pixel);
                                            using (var newStream = FaceHelper.ToStream(croppedImage, src.RawFormat))
                                            {
                                                Task t1 = blobManager.UploadAsync(doc.ResultContainerName, resultBlobName, newStream);
                                                t1.Wait();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            log.Info($"SaveResultImage requestId: {requestID} no detection for {doc.BlobName} ticks: {DateTime.Now.Ticks}");
                        }
                    }
                }
                else
                {
                    log.Info($"SaveResultImage  requestId: {requestID} no documents received  ticks: {DateTime.Now.Ticks}");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Exception Message: {ex.Message}, requestId: {requestID}, ticks: {DateTime.Now.Ticks}", ex);
                strException = "unsuccessfully";
            }
            log.Info($"Finished {strException} SaveResultImage requestId: {requestID} ticks: {DateTime.Now.Ticks}");
        }
    }
}
