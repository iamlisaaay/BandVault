using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace BandVault.Web.Services
{
    // ============================================
    // C19: СТАТИЧНИЙ КОНТЕНТ У BLOB STORAGE (Azure / Azurite) + опційний CDN
    // Storage:PublicBaseUrl - адреса, за якою браузер бачить файли (CDN-ендпоінт або Azurite з localhost)
    // ============================================
    public class AzureBlobService : IFileService
    {
        private readonly BlobServiceClient _service;
        private readonly BlobContainerClient _container;
        private readonly string? _publicBaseUrl;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _initialized;

        public AzureBlobService(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("AzureBlobStorage");
            var containerName = configuration["Storage:ContainerName"] ?? "bandvault-media";
            _service = new BlobServiceClient(connectionString);
            _container = _service.GetBlobContainerClient(containerName);
            var publicBaseUrl = configuration["Storage:PublicBaseUrl"];
            _publicBaseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.TrimEnd('/');
        }

        public async Task<string> UploadFileAsync(IFormFile file, string folderName)
        {
            if (file == null || file.Length == 0) throw new ArgumentException("Файл порожній", nameof(file));

            using var stream = file.OpenReadStream();
            return await UploadStreamAsync(stream, folderName, Path.GetExtension(file.FileName), file.ContentType);
        }

        public async Task<string> UploadBytesAsync(byte[] data, string folderName, string extension, string contentType)
        {
            using var stream = new MemoryStream(data);
            return await UploadStreamAsync(stream, folderName, extension, contentType);
        }

        private async Task<string> UploadStreamAsync(Stream stream, string folderName, string extension, string contentType)
        {
            await EnsureContainerAsync();

            string blobName = $"{folderName}/{Guid.NewGuid()}{extension.ToLowerInvariant()}";
            var blobClient = _container.GetBlobClient(blobName);

            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                // Імена файлів унікальні (GUID), тож CDN/браузер можуть кешувати їх "назавжди"
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType,
                    CacheControl = "public, max-age=31536000, immutable"
                }
            });

            return _publicBaseUrl != null
                ? $"{_publicBaseUrl}/{_container.Name}/{blobName}"
                : blobClient.Uri.ToString();
        }

        public async Task DeleteFileAsync(string fileUrl)
        {
            var blobName = GetBlobName(fileUrl);
            if (blobName == null) return; // старі локальні файли (/images/...) у сховищі не живуть
            try
            {
                await _container.GetBlobClient(blobName).DeleteIfExistsAsync();
            }
            catch
            {
                // Видалення медіа не має ламати видалення сутності
            }
        }

        // Ім'я блоба - усе після "/{container}/" в абсолютному URL (працює і для PublicBaseUrl, і для прямого URI)
        private string? GetBlobName(string fileUrl)
        {
            if (string.IsNullOrEmpty(fileUrl) || !Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri)) return null;
            var marker = $"/{_container.Name}/";
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            var index = path.IndexOf(marker, StringComparison.Ordinal);
            return index < 0 ? null : path[(index + marker.Length)..];
        }

        // Аудіо зі сховища відтворюється через Web Audio API (візуалізатор F6/C15).
        // Для ресурсів з іншого домену браузер вимагає CORS, інакше звук глушиться.
        private async Task EnsureCorsAsync()
        {
            try
            {
                var properties = (await _service.GetPropertiesAsync()).Value;
                if (properties.Cors.Any(r => r.AllowedOrigins == "*")) return;
                properties.Cors.Add(new BlobCorsRule
                {
                    AllowedOrigins = "*",
                    AllowedMethods = "GET,HEAD",
                    AllowedHeaders = "*",
                    ExposedHeaders = "*",
                    MaxAgeInSeconds = 3600
                });
                await _service.SetPropertiesAsync(properties);
            }
            catch
            {
                // SAS/ключ без прав на властивості сервісу - CORS налаштовується вручну в порталі Azure
            }
        }

        private async Task EnsureContainerAsync()
        {
            if (_initialized) return;
            await _initLock.WaitAsync();
            try
            {
                if (_initialized) return;
                // Робимо файли публічно доступними для читання
                await _container.CreateIfNotExistsAsync(PublicAccessType.Blob);
                await EnsureCorsAsync();
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }
    }
}
