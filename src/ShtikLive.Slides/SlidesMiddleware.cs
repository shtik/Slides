using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.AzureAppServices.Internal;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Polly;
using Polly.CircuitBreaker;
using ShtikLive.Slides.Options;

namespace ShtikLive.Slides
{
    public class SlidesMiddleware
    {
        private readonly ILogger<SlidesMiddleware> _logger;
        private readonly CloudBlobClient _client;
        private readonly Policy _postPolicy;
        private readonly Policy _getPolicy;

        // ReSharper disable once UnusedParameter.Local
        public SlidesMiddleware(RequestDelegate _, IOptions<StorageOptions> options, ILogger<SlidesMiddleware> logger)
        {
            _logger = logger;
            var storageAccount = CloudStorageAccount.Parse(options.Value.ConnectionString);
            _client = storageAccount.CreateCloudBlobClient();
            _postPolicy = ResiliencePolicy.Create(logger);
            _getPolicy = ResiliencePolicy.Create(logger);
            _logger.LogWarning(nameof(SlidesMiddleware));
        }

        public Task Invoke(HttpContext context)
        {

            _logger.LogWarning("Path: {path}", context.Request.Path);
            var path = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ??
                       Array.Empty<string>();
            if (path.Length == 3)
            {
                if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    return Get(path[0], path[1], path[2], context);
                }
                if (context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    return Post(path[0], path[1], path[2], context);
                }
                if (context.Request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return Head(path[0], path[1], path[2], context);
                }
            }
            _logger.LogError("Path not found: {path}", context.Request.Path);
            context.Response.StatusCode = 404;
            return Task.CompletedTask;
        }

        private Task Post(string presenter, string show, string index, HttpContext context)
        {
            return _postPolicy.ExecuteAsync(async () =>
            {
                byte[] content;
                using (var stream = new MemoryStream(32768))
                {
                    await context.Request.Body.CopyToAsync(stream);
                    content = stream.ToArray();
                }
                var containerRef = _client.GetContainerReference(presenter);
                await containerRef.CreateIfNotExistsAsync(BlobContainerPublicAccessType.Off, null, null);
                var directory = containerRef.GetDirectoryReference(show);
                var blob = directory.GetBlockBlobReference($"{index}.jpg");
                blob.Properties.ContentType = context.Request.ContentType;
                await blob.UploadFromByteArrayAsync(content, 0, content.Length);
                context.Response.StatusCode = 201;
            });
        }

        private Task Get(string presenter, string show, string index, HttpContext context)
        {
            return _getPolicy.ExecuteAsync(async () =>
            {
                var containerRef = _client.GetContainerReference(presenter);
                if (await containerRef.ExistsAsync())
                {
                    var directory = containerRef.GetDirectoryReference(show);
                    var blob = directory.GetBlockBlobReference($"{index}.jpg");
                    if (await blob.ExistsAsync())
                    {
                        await blob.FetchAttributesAsync();
                        context.Response.Headers.ContentLength = blob.Properties.Length;
                        context.Response.Headers["Content-Type"] = blob.Properties.ContentType;
                        context.Response.Headers["ETag"] = blob.Properties.ETag;
                        context.Response.StatusCode = 200;
                        await blob.DownloadToStreamAsync(context.Response.Body);
                        return;
                    }
                }
                context.Response.StatusCode = 404;
            });
        }

        private Task Head(string presenter, string show, string index, HttpContext context)
        {
            return _getPolicy.ExecuteAsync(async () =>
            {
                var containerRef = _client.GetContainerReference(presenter);
                if (await containerRef.ExistsAsync())
                {
                    var directory = containerRef.GetDirectoryReference(show);
                    var blob = directory.GetBlockBlobReference($"{index}.jpg");
                    if (await blob.ExistsAsync())
                    {
                        await blob.FetchAttributesAsync();
                        context.Response.Headers.ContentLength = blob.Properties.Length;
                        context.Response.Headers["Content-Type"] = blob.Properties.ContentType;
                        context.Response.Headers["ETag"] = blob.Properties.ETag;
                        context.Response.StatusCode = 200;
                        return;
                    }
                }
                context.Response.StatusCode = 404;
            });
        }
    }

    public static class ResiliencePolicy
    {
        public static Policy Create(ILogger logger)
        {
            var retry = Policy.Handle<Exception>(e => !(e is BrokenCircuitException))
                .WaitAndRetryForeverAsync(n => TimeSpan.FromMilliseconds(Math.Min((n + 1) * 100, 500)),
                    (result, _) =>
                    {
                        if (result != null)
                            logger.LogWarning(result, result.Message);
                    });

            var breaker = Policy.Handle<Exception>()
                .CircuitBreakerAsync(5, TimeSpan.FromMinutes(1),
                    (result, _) => logger.LogWarning("Circuit breaker tripped."),
                    () => logger.LogWarning("Circuit breaker reset."));

            return Policy.WrapAsync(retry, breaker);
        }
    }
}