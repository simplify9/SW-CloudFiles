using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.Runtime.Internal.Transform;
using Amazon.S3;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SW.CloudFiles.S3;

/// <summary>
/// The <see cref="AmazonS3Client"/> CloudFiles uses, with x-amz-expiration stripped from responses.
/// </summary>
internal class CloudFilesS3Client : AmazonS3Client
{
    public CloudFilesS3Client(string accessKeyId, string secretAccessKey, AmazonS3Config config)
        : base(accessKeyId, secretAccessKey, config)
    {
    }

    protected override void CustomizeRuntimePipeline(RuntimePipeline pipeline)
    {
        base.CustomizeRuntimePipeline(pipeline);
        // Inner to the unmarshaller, so it sees the response first.
        pipeline.AddHandlerAfter<Unmarshaller>(new DropExpirationHeaderHandler());
    }
}

/// <summary>
/// Hides the x-amz-expiration header from the unmarshaller. The SDK parses its expiry-date strictly and
/// throws a FormatException when a provider sends a wrong weekday, failing a request that already
/// succeeded. CloudFiles never reads that header, so it is dropped rather than parsed.
/// </summary>
internal class DropExpirationHeaderHandler : PipelineHandler
{
    internal const string HeaderName = "x-amz-expiration";

    public override void InvokeSync(IExecutionContext executionContext)
    {
        base.InvokeSync(executionContext);
        DropHeader(executionContext);
    }

    public override async Task<T> InvokeAsync<T>(IExecutionContext executionContext)
    {
        var response = await base.InvokeAsync<T>(executionContext).ConfigureAwait(false);
        DropHeader(executionContext);
        return response;
    }

    private static void DropHeader(IExecutionContext executionContext)
    {
        var responseContext = executionContext.ResponseContext;
        if (responseContext.HttpResponse?.IsHeaderPresent(HeaderName) == true)
            responseContext.HttpResponse = new ResponseWithoutHeader(responseContext.HttpResponse, HeaderName);
    }

    private class ResponseWithoutHeader : IWebResponseData
    {
        private readonly IWebResponseData inner;
        private readonly string header;

        public ResponseWithoutHeader(IWebResponseData inner, string header)
        {
            this.inner = inner;
            this.header = header;
        }

        public long ContentLength => inner.ContentLength;
        public string ContentType => inner.ContentType;
        public HttpStatusCode StatusCode => inner.StatusCode;
        public bool IsSuccessStatusCode => inner.IsSuccessStatusCode;
        public IHttpResponseBody ResponseBody => inner.ResponseBody;

        public string[] GetHeaderNames() =>
            inner.GetHeaderNames().Where(name => !IsDropped(name)).ToArray();

        public bool IsHeaderPresent(string headerName) =>
            !IsDropped(headerName) && inner.IsHeaderPresent(headerName);

        public string GetHeaderValue(string headerName) =>
            IsDropped(headerName) ? string.Empty : inner.GetHeaderValue(headerName);

        private bool IsDropped(string headerName) =>
            string.Equals(headerName, header, StringComparison.OrdinalIgnoreCase);
    }
}
