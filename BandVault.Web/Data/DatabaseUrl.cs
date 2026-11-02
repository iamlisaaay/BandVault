using Npgsql;

namespace BandVault.Web.Data
{
    // C2: Render/Railway/Heroku надають рядок підключення як postgres://user:pass@host:port/db.
    // Npgsql очікує формат "Host=...;Username=...", тож конвертуємо, якщо задано DATABASE_URL.
    public static class DatabaseUrl
    {
        public static string? Resolve(IConfiguration configuration)
        {
            var url = configuration["DATABASE_URL"];
            if (string.IsNullOrWhiteSpace(url)) return configuration.GetConnectionString("DefaultConnection");

            var uri = new Uri(url);
            var credentials = uri.UserInfo.Split(':', 2);
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
                Database = uri.AbsolutePath.TrimStart('/'),
                Username = Uri.UnescapeDataString(credentials[0]),
                Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
                SslMode = configuration["DATABASE_SSLMODE"] == "Disable" ? SslMode.Disable : SslMode.Prefer
            };
            return csb.ConnectionString;
        }
    }
}
