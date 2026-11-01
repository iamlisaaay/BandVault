using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace BandVault.Web.Services
{
    public class AzureBlobService : IFileService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _containerName = "bandvault-media"; // Назва контейнера в Azure

        public AzureBlobService(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("AzureBlobStorage");
            _blobServiceClient = new BlobServiceClient(connectionString);
        }

        public async Task<string> UploadFileAsync(IFormFile file, string folderName)
        {
            if (file == null || file.Length == 0) return null;

            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            // Робимо файли публічно доступними для читання
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            // Генеруємо унікальне ім'я файлу
            string fileName = $"{folderName}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType }
            });

            return blobClient.Uri.ToString(); // Повертаємо абсолютний URL файлу
        }

        public async Task DeleteFileAsync(string fileUrl)
        {
            if (string.IsNullOrEmpty(fileUrl)) return;
            try
            {
                var uri = new Uri(fileUrl);
                // Витягуємо ім'я файлу з URL (все, що після назви контейнера)
                string blobName = string.Join("", uri.Segments.Skip(2));
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var blobClient = containerClient.GetBlobClient(blobName);
                await blobClient.DeleteIfExistsAsync();
            }
            catch
            {
                // Логування помилки видалення можна додати сюди
            }
        }
    }
}