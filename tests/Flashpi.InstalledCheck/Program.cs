using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Flashpi;
using Flashpi.Models;

// The installed package works: its client, its JSON metadata and its webhook verification.
const string key = "fp_test_aaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

using var http = new HttpClient(new Answer());
using var flashpi = new FlashpiClient(new FlashpiClientOptions { ApiKey = key, HttpClient = http });
Check(flashpi.Environment == FlashpiEnvironment.Test, "environment");
Check(!flashpi.ToString().Contains("fp_test"), "ToString hides the key");

var status = await flashpi.Status.GetAsync();
Check(status.Status == ApiStatusKind.Operational, "status");

var webhook = Webhooks.Unwrap(
    "{\"id\":\"019a1b2c-3d4e-7f60-8a1b-2c3d4e5f6a7b\",\"type\":\"invoice.authorized\",\"environment\":\"test\",\"createdAt\":\"2026-10-16T17:20:00.000Z\"}",
    new Dictionary<string, string>
    {
        ["webhook-id"] = "019a1b2c-3d4e-7f60-8a1b-2c3d4e5f6a7b",
        ["webhook-timestamp"] = "1792152000",
        ["webhook-signature"] = "v1,HbWOpDPWCo3wtq41t9GVHUpg69M1zgvveoDJmSWkA8U=",
    },
    "whsec_Zmxhc2hwaS1kb2NzLWV4YW1wbGUtc2VjcmV0LTMyLWI=",
    now: DateTimeOffset.FromUnixTimeSeconds(1792152000));
Check(webhook is EventThin { Type.Value: "invoice.authorized" }, "webhook");

Console.WriteLine($"Flashpi works on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
return 0;

static void Check(bool condition, string what)
{
    if (!condition)
    {
        throw new InvalidOperationException($"The installed package failed: {what}");
    }
}

internal sealed class Answer : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"operational\"}", Encoding.UTF8, "application/json"),
        });
}
