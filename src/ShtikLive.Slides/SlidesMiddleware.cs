using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using ShtikLive.Slides.Options;

namespace ShtikLive.Slides
{
    public class SlidesMiddleware
    {
        private readonly CloudBlobClient _client;

        public SlidesMiddleware(RequestDelegate _, IOptions<StorageOptions> options)
        {
            var storageAccount = CloudStorageAccount.Parse(options.Value.ConnectionString);
            _client = storageAccount.CreateCloudBlobClient();
        }

        public Task Invoke(HttpContext context)
        {
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
            }
            context.Response.StatusCode = 404;
            return Task.CompletedTask;
        }

        private async Task Post(string presenter, string show, string index, HttpContext context)
        {
            var containerRef = _client.GetContainerReference(presenter);
            await containerRef.CreateIfNotExistsAsync(BlobContainerPublicAccessType.Off, null, null);
            var directory = containerRef.GetDirectoryReference(show);
            var blob = directory.GetBlockBlobReference($"{index}.jpg");
            blob.Properties.ContentType = context.Request.ContentType;
            await blob.UploadFromStreamAsync(context.Request.Body);
            context.Response.StatusCode = 201;
        }

        private async Task Get(string presenter, string show, string index, HttpContext context)
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
                    context.Response.StatusCode = 200;
                    await blob.DownloadToStreamAsync(context.Response.Body);
                    return;
                }
            }
            context.Response.StatusCode = 404;
        }
    }
}