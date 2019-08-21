using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using PdfConverterFunction.Models;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

namespace PdfConverterFunction
{
    public class PdfConverterFunction
    {
#if DEBUG
        const string LIBRE_OFFICE_BIN = @"C:\Program Files\LibreOffice\program\soffice.exe";
#else        
        const string LIBRE_OFFICE_BIN = "/usr/bin/libreoffice";
#endif
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<BlobStorageConfiguration> _blobStorageConfiguration;

        public PdfConverterFunction(IHttpClientFactory httpClientFactory, IOptions<BlobStorageConfiguration> blobStorageConfiguration)
        {
            _httpClientFactory = httpClientFactory;
            _blobStorageConfiguration = blobStorageConfiguration;
        }
        
        [FunctionName("PdfConverterFunction")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            // Get source file url to download
            using (var bodyReader = new StreamReader(req.Body))
            {
                var sourceUrl = JObject.Parse(bodyReader.ReadToEnd())["url"].Value<string>();

                // Download source file and store it in a temporal file
                var sourceFileName = Path.GetTempFileName();
                var httpClient = _httpClientFactory.CreateClient("Resillient");
                using (HttpResponseMessage response = await httpClient.GetAsync(sourceUrl))
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return new NotFoundObjectResult("Source file not found");
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        return new BadRequestObjectResult($"Cannot download source file: {response.StatusCode} {response.ReasonPhrase}");
                    }
                    using (var sourceFile = File.OpenWrite(sourceFileName))
                    {
                        await response.Content.CopyToAsync(sourceFile);
                        await sourceFile.FlushAsync();
                    }
                }

                // Convert file to PDF using libreoffice
                var pdfProcess = new Process();
                pdfProcess.StartInfo.FileName = LIBRE_OFFICE_BIN;
                pdfProcess.StartInfo.Arguments = $"--norestore --nofirststartwizard --headless --convert-to pdf \"{sourceFileName}\"";
                pdfProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(sourceFileName); //This is really important
                pdfProcess.Start();

                // Wait while document is converting
                while (pdfProcess.IsRunning())
                {
                    await Task.Delay(500);
                }

                // Check if file was converted properly
                var destinationFileName = $"{Path.Combine(Path.GetDirectoryName(sourceFileName), Path.GetFileNameWithoutExtension(sourceFileName))}.pdf";
                if (!File.Exists(destinationFileName))
                {
                    return new BadRequestObjectResult("Error converting file to PDF");
                }

                // Store it to temporary blob storage
                var storageAccount = CloudStorageAccount.Parse(_blobStorageConfiguration.Value.ConnectionString);
                var blobClient = storageAccount.CreateCloudBlobClient();
                var container = blobClient.GetContainerReference($"converted-files");
                container.CreateIfNotExists(BlobContainerPublicAccessType.Blob);

                var cloudBlockBlob = container.GetBlockBlobReference($"{Guid.NewGuid()}.pdf");
                await cloudBlockBlob.UploadFromFileAsync(destinationFileName);

                // Get a secure link for 24hrs and return it
                var sasToken = cloudBlockBlob.GetSharedAccessSignature(new SharedAccessBlobPolicy()
                {
                    Permissions = SharedAccessBlobPermissions.Read,
                    SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-5),
                    SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24),
                });

                // Cleanup
                try
                {
                    File.Delete(destinationFileName);
                    File.Delete(sourceFileName);
                }
                catch { }

                // Return secure link
                return new OkObjectResult($"{cloudBlockBlob.Uri}{sasToken}");
            }
        }
    }
}
