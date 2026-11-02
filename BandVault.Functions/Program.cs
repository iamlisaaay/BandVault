using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

// ============================================
// B7: SERVERLESS API BANDVAULT (Azure Functions, isolated worker)
//  POST /api/moderation/check  - перевірка повідомлення фаната
//  POST /api/promo/generate     - генерація підписаного промокоду (function key)
//  GET  /api/promo/validate     - перевірка промокоду
// ============================================
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.Build().Run();
