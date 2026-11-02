namespace BandVault.Web.Services
{
    public interface IFileService
    {
        Task<string> UploadFileAsync(IFormFile file, string folderName);
        Task<string> UploadBytesAsync(byte[] data, string folderName, string extension, string contentType);
        Task DeleteFileAsync(string fileUrl);
    }
}
