using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DetectionApp
{
    public class BlobManager
    {
        private readonly string _blobStorageConn;
        public BlobManager(string conn)
        {
            _blobStorageConn = conn;
        }

        private async Task<CloudBlobContainer> GetContainerAsync(string containerName)
        {
            //Account  
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(_blobStorageConn);
            //Client  
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            //Container  
            CloudBlobContainer blobContainer =
                blobClient.GetContainerReference(containerName);
            await blobContainer.CreateIfNotExistsAsync();
            return blobContainer;
        }

        private async Task<CloudBlockBlob> GetBlockBlobAsync(string containerName, string blobName)
        {
            //Container  
            CloudBlobContainer blobContainer = await GetContainerAsync(containerName);
            //Blob  
            CloudBlockBlob blockBlob = blobContainer.GetBlockBlobReference(blobName);
            return blockBlob;
        }

        private async Task<List<AzureBlobItem>> GetBlobListAsync(string containerName, bool useFlatListing = true)
        {
            //Container  
            CloudBlobContainer blobContainer = await GetContainerAsync(containerName);
            //List  
            var list = new List<AzureBlobItem>();
            BlobContinuationToken token = null;
            do
            {
                BlobResultSegment resultSegment =
                    await blobContainer.ListBlobsSegmentedAsync("", useFlatListing, new BlobListingDetails(), null, token, null, null);
                token = resultSegment.ContinuationToken;

                foreach (IListBlobItem item in resultSegment.Results)
                {
                    list.Add(new AzureBlobItem(item));
                }
            } while (token != null);

            return list.OrderBy(i => i.Folder).ThenBy(i => i.Name).ToList();
        }

        public async Task UploadAsync(string containerName, string blobName, string filePath)
        {
            //Blob  
            CloudBlockBlob blockBlob = await GetBlockBlobAsync(containerName, blobName);
            //Upload  
            using (var fileStream = System.IO.File.Open(filePath, FileMode.Open))
            {
                fileStream.Position = 0;
                await blockBlob.UploadFromStreamAsync(fileStream);
            }
        }

        public async Task UploadAsync(string containerName, string blobName, Stream stream)
        {
            //Blob  
            CloudBlockBlob blockBlob = await GetBlockBlobAsync(containerName, blobName);

            //Upload  
            stream.Position = 0;
            await blockBlob.UploadFromStreamAsync(stream);
        }

        public async Task<MemoryStream> DownloadAsync(string containerName, string blobName)
        {
            //Blob  
            CloudBlockBlob blockBlob = await GetBlockBlobAsync(containerName, blobName);
            //Download  
            var stream = new MemoryStream();
            
            await blockBlob.DownloadToStreamAsync(stream);
            return stream;
            
        }

        public async Task DownloadAsync(string containerName, string blobName, string path)
        {
            //Blob  
            CloudBlockBlob blockBlob = await GetBlockBlobAsync(containerName, blobName);
            //Download  
            await blockBlob.DownloadToFileAsync(path, FileMode.Create);
        }

        public async Task<List<AzureBlobItem>> ListAsync(string containerName)
        {
            return await GetBlobListAsync(containerName);
        }

        public async Task<List<string>> ListFoldersAsync(string containerName)
        {
            var list = await GetBlobListAsync(containerName);
            return list.Where(i => !string.IsNullOrEmpty(i.Folder))
                       .Select(i => i.Folder)
                       .Distinct()
                       .OrderBy(i => i)
                       .ToList();
        }
    }       

    public class AzureBlobItem
    {
        public AzureBlobItem(IListBlobItem item)
        {
            this.Item = item;
        }

        public IListBlobItem Item { get; }

        public bool IsBlockBlob => Item.GetType() == typeof(CloudBlockBlob);
        public bool IsPageBlob => Item.GetType() == typeof(CloudPageBlob);
        public bool IsDirectory => Item.GetType() == typeof(CloudBlobDirectory);

        public string BlobName => IsBlockBlob ? ((CloudBlockBlob)Item).Name :
                                    IsPageBlob ? ((CloudPageBlob)Item).Name :
                                    IsDirectory ? ((CloudBlobDirectory)Item).Prefix :
                                    "";

        public string Folder => BlobName.Contains("/") ?
                            BlobName.Substring(0, BlobName.LastIndexOf("/")) : "";

        public string Name => BlobName.Contains("/") ?
                            BlobName.Substring(BlobName.LastIndexOf("/") + 1) : BlobName;
    }
}

