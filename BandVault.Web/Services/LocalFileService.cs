namespace BandVault.Web.Services
{
    // Запасний варіант для локальної розробки без Azurite: файли лягають у wwwroot/uploads
    // Вмикається Storage:Provider = "Local" (або коли рядок підключення до Blob порожній)
    public class LocalFileService : IFileService
    {
        private readonly IWebHostEnvironment _env;

        public LocalFileService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task<string> UploadFileAsync(IFormFile file, string folderName)
        {
            if (file == null || file.Length == 0) throw new ArgumentException("Файл порожній", nameof(file));

            var (fullPath, url) = BuildPath(folderName, Path.GetExtension(file.FileName));
            using var stream = new FileStream(fullPath, FileMode.Create);
            await file.CopyToAsync(stream);
            return url;
        }

        public async Task<string> UploadBytesAsync(byte[] data, string folderName, string extension, string contentType)
        {
            var (fullPath, url) = BuildPath(folderName, extension);
            await File.WriteAllBytesAsync(fullPath, data);
            return url;
        }

        public Task DeleteFileAsync(string fileUrl)
        {
            if (string.IsNullOrEmpty(fileUrl) || !fileUrl.StartsWith("/uploads/")) return Task.CompletedTask;

            var fullPath = Path.GetFullPath(Path.Combine(_env.WebRootPath, fileUrl.TrimStart('/')));
            var uploadsRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads"));
            if (fullPath.StartsWith(uploadsRoot) && File.Exists(fullPath)) File.Delete(fullPath);
            return Task.CompletedTask;
        }

        private (string FullPath, string Url) BuildPath(string folderName, string extension)
        {
            var dir = Path.Combine(_env.WebRootPath, "uploads", folderName);
            Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid()}{extension.ToLowerInvariant()}";
            return (Path.Combine(dir, fileName), $"/uploads/{folderName}/{fileName}");
        }
    }
}
