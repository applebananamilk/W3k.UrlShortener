using Base62;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Serilog.Events;
using ServiceSelf;
using System.Text;

namespace W3k.UrlShortener;

public class Program
{
    public static void Main(string[] args)
    {
        string appName = "W3k.UrlShortener";

        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Async(c => c.File($"{AppContext.BaseDirectory}/Logs/logs.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        try
        {
            var serviceOptions = new ServiceOptions
            {
                Description = appName
            };
            serviceOptions.Linux.Service.Restart = "always";
            serviceOptions.Linux.Service.RestartSec = "10";
            serviceOptions.Windows.DisplayName = appName;
            serviceOptions.Windows.FailureActionType = WindowsServiceActionType.Restart;

            if (Service.UseServiceSelf(args, appName, serviceOptions))
            {
                Log.Information($"{appName} running");

                var builder = WebApplication.CreateBuilder(args);
                builder.Host.UseSerilog().UseServiceSelf();

                builder.Services.AddMemoryCache();
                builder.Services.AddDbContextPool<UrlShortenerDbContext>(options =>
                {
                    options.UseSqlite(builder.Configuration.GetConnectionString("Default"));
                });

                var app = builder.Build();

                using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
                {
                    var context = serviceScope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
                    context.Database.EnsureCreated();
                }

                app.UseDefaultFiles();
                app.UseStaticFiles();

                app.MapGet("/{key}", async (string key, HttpContext httpContext) =>
                {
                    if (string.IsNullOrEmpty(key))
                    {
                        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                        await httpContext.Response.WriteAsync("NotFound");
                        return;
                    }

                    var cache = httpContext.RequestServices.GetRequiredService<IMemoryCache>();
                    string cacheKey = $"s:{key}";
                    string cacheVal = await cache.GetOrCreateAsync(cacheKey, async entry =>
                    {
                        var db = httpContext.RequestServices.GetRequiredService<UrlShortenerDbContext>();
                        var urlMapping = await db.UrlMappings.AsNoTracking().FirstOrDefaultAsync(p => p.Key == key);
                        if (urlMapping == null)
                        {
                            entry.AbsoluteExpirationRelativeToNow = TimeSpan.Zero;
                        }
                        return urlMapping?.OriginalUrl;
                    });

                    if (string.IsNullOrEmpty(cacheVal))
                    {
                        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                        await httpContext.Response.WriteAsync("NotFound");
                        return;
                    }

                    var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();

                    httpContext.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                    httpContext.Response.Headers.TryAdd("Location", cacheVal);
                    return;
                });

                app.MapPost("/api/v1/shorten", async ([FromBody] ShortenReq req, [FromServices] IConfiguration configuration, [FromServices] UrlShortenerDbContext db) =>
                {
                    var result = new Result();

                    string originalUrl = req?.OriginalUrl;

                    if (string.IsNullOrEmpty(originalUrl))
                    {
                        return new Result { Succeeded = false, Message = "The URL cannot be empty" };
                    }

                    if (Uri.TryCreate(originalUrl, UriKind.Absolute, out var uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                    {
                        string key = GenerateKey(originalUrl);

                        var urlMapping = await db.UrlMappings.FirstOrDefaultAsync(p => p.Key == key);

                        if (urlMapping != null)
                        {
                            if (urlMapping.OriginalUrl != originalUrl)
                            {
                                // TODO Duplicate key
                                return new Result { Succeeded = false, Message = "error!" };
                            }
                        }
                        else
                        {
                            urlMapping = new UrlMapping(key, originalUrl);
                            await db.UrlMappings.AddAsync(urlMapping);
                            await db.SaveChangesAsync();
                        }

                        return new Result { Succeeded = true, Data = $"{configuration["UrlShortenerSettings:Domain"]}/{key}" };
                    }
                    else
                    {
                        return new Result { Succeeded = false, Message = "Please enter the URL in the correct format" };
                    }

                    static string GenerateKey(string originalUrl)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(originalUrl);
                        int hash = (int)HashDepot.MurmurHash3.Hash32(bytes, 10010);
                        string key = hash.ToBase62();
                        return key;
                    }
                });

                app.Run();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, $"{appName} terminated unexpectedly!");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    class ShortenReq
    {
        public string OriginalUrl { get; set; }
    }

    class Result
    {
        public bool Succeeded { get; set; }

        public string Message { get; set; }

        public object Data { get; set; }
    }
}
