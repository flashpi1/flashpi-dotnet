using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Flashpi.Internal;
using Flashpi.Models;
using Xunit;

namespace Flashpi.Tests;

/// <summary>
/// Every operation of the API's OpenAPI document maps to exactly one method of the client, which
/// sends its HTTP method, path, query and body, and reads its answer.
/// </summary>
public sealed class OperationsTests
{
    private const string Id1 = "id-1";
    private const string Id2 = "id-2";
    private const string Id3 = "id-3";

    private static readonly byte[] P12 = [0x30, 0x82, 0x01, 0x02];

    /// <summary>Each operationId of the contract and a call of the method that sends it.</summary>
    private static readonly Dictionary<string, Func<FlashpiClient, Task>> Operations = new()
    {
        ["statusGet"] = client => client.Status.GetAsync(),
        ["issuersCreate"] = client => client.Issuers.CreateAsync(Empty<CreateIssuer>()),
        ["issuersList"] = async client => await client.Issuers.ListAsync(new IssuerListParams { Limit = 1, Cursor = "c" }),
        ["issuersGet"] = client => client.Issuers.GetAsync(Id1),
        ["issuersUpdate"] = client => client.Issuers.UpdateAsync(Id1, new UpdateIssuer()),
        ["issuersDelete"] = client => client.Issuers.DeleteAsync(Id1),
        ["establishmentsCreate"] = client => client.Issuers.Establishments.CreateAsync(Id1, new CreateEstablishment { Code = "001", Address = "Quito" }),
        ["establishmentsList"] = async client => await client.Issuers.Establishments.ListAsync(Id1, new EstablishmentListParams { Limit = 1, Cursor = "c" }),
        ["establishmentsGet"] = client => client.Issuers.Establishments.GetAsync(Id1, Id2),
        ["establishmentsUpdate"] = client => client.Issuers.Establishments.UpdateAsync(Id1, Id2, new UpdateEstablishment()),
        ["establishmentsDelete"] = client => client.Issuers.Establishments.DeleteAsync(Id1, Id2),
        ["emissionPointsCreate"] = client => client.Issuers.Establishments.EmissionPoints.CreateAsync(Id1, Id2, new CreateEmissionPoint { Code = "001" }),
        ["emissionPointsList"] = async client => await client.Issuers.Establishments.EmissionPoints.ListAsync(Id1, Id2, new EmissionPointListParams { Limit = 1, Cursor = "c" }),
        ["emissionPointsGet"] = client => client.Issuers.Establishments.EmissionPoints.GetAsync(Id1, Id2, Id3),
        ["emissionPointsDelete"] = client => client.Issuers.Establishments.EmissionPoints.DeleteAsync(Id1, Id2, Id3),
        ["certificatesUpload"] = client => client.Issuers.Certificates.UploadAsync(Id1, new CertificateUpload(P12, "x")),
        ["certificatesList"] = async client => await client.Issuers.Certificates.ListAsync(Id1, new CertificateListParams { Limit = 1, Cursor = "c" }),
        ["certificatesGet"] = client => client.Issuers.Certificates.GetAsync(Id1, Id2),
        ["issuerOnboardingsCreate"] = client => client.IssuerOnboardings.CreateAsync(new CertificateUpload(P12, "x")),
        ["issuerOnboardingsGet"] = client => client.IssuerOnboardings.GetAsync(Id1),
        ["issuerOnboardingsComplete"] = client => client.IssuerOnboardings.CompleteAsync(Id1),
        ["invoicesCreate"] = client => client.Invoices.CreateAsync(Empty<CreateInvoice>()),
        ["creditNotesCreate"] = client => client.CreditNotes.CreateAsync(Empty<CreateCreditNote>()),
        ["debitNotesCreate"] = client => client.DebitNotes.CreateAsync(Empty<CreateDebitNote>()),
        ["purchaseSettlementsCreate"] = client => client.PurchaseSettlements.CreateAsync(Empty<CreatePurchaseSettlement>()),
        ["waybillsCreate"] = client => client.Waybills.CreateAsync(Empty<CreateWaybill>()),
        ["withholdingsCreate"] = client => client.Withholdings.CreateAsync(Empty<CreateWithholding>()),
        ["documentsList"] = async client => await client.Documents.ListAsync(new DocumentListParams
        {
            Limit = 1,
            Cursor = "c",
            IssuerId = "i",
            Type = DocumentType.Invoice,
            Status = DocumentStatus.Authorized,
            IssueDateFrom = "2026-11-01",
            IssueDateTo = "2026-11-30",
            CounterpartyId = "1790000000001",
            AccessKey = "1",
        }),
        ["documentsGet"] = client => client.Documents.GetAsync(Id1),
        ["documentsGetXml"] = client => client.Documents.GetXmlAsync(Id1),
        ["documentsGetPdf"] = client => client.Documents.GetPdfAsync(Id1),
        ["documentsSendEmail"] = client => client.Documents.SendEmailAsync(Id1),
        ["webhookEndpointsCreate"] = client => client.WebhookEndpoints.CreateAsync(new CreateWebhookEndpoint { Url = "https://example.com", EnabledEvents = [EventType.All] }),
        ["webhookEndpointsList"] = async client => await client.WebhookEndpoints.ListAsync(new WebhookEndpointListParams { Limit = 1, Cursor = "c" }),
        ["webhookEndpointsGet"] = client => client.WebhookEndpoints.GetAsync(Id1),
        ["webhookEndpointsUpdate"] = client => client.WebhookEndpoints.UpdateAsync(Id1, new UpdateWebhookEndpoint()),
        ["webhookEndpointsDelete"] = client => client.WebhookEndpoints.DeleteAsync(Id1),
        ["webhookEndpointsSecret"] = client => client.WebhookEndpoints.GetSecretAsync(Id1),
        ["webhookEndpointsRotateSecret"] = client => client.WebhookEndpoints.RotateSecretAsync(Id1, new RotateWebhookEndpointSecret { PreviousSecretExpiresInHours = 1 }),
        ["webhookEndpointsVerify"] = client => client.WebhookEndpoints.VerifyAsync(Id1),
        ["webhookEndpointsDeliveriesList"] = async client => await client.WebhookEndpoints.Deliveries.ListAsync(Id1, new WebhookDeliveryListParams
        {
            Limit = 1,
            Cursor = "c",
            Status = WebhookDeliveryStatus.Failed,
            EventType = EventType.InvoiceAuthorized,
            EventId = "e",
        }),
        ["webhookEndpointsDeliveriesGet"] = client => client.WebhookEndpoints.Deliveries.GetAsync(Id1, Id2),
        ["webhookEndpointsDeliveriesReplay"] = client => client.WebhookEndpoints.Deliveries.ReplayAsync(Id1, Id2),
        ["eventsList"] = async client => await client.Events.ListAsync(new EventListParams
        {
            Limit = 1,
            Cursor = "c",
            Type = EventType.InvoiceAuthorized,
            IssuerId = "i",
            DocumentId = "d",
            CertificateId = "c",
        }),
        ["eventsGet"] = client => client.Events.GetAsync(Id1),
    };

    private sealed record Operation(
        string Id,
        string Method,
        string Path,
        HashSet<string> Query,
        string? BodyType,
        int Status,
        string? AnswerType,
        bool Idempotent);

    private static readonly Dictionary<string, Operation> Contract = ReadContract();

    public static TheoryData<string> OperationIds => [.. Contract.Keys.OrderBy(id => id, StringComparer.Ordinal)];

    [Fact]
    public void Maps_every_operation_of_the_contract_and_no_other()
    {
        Assert.Equal(45, Contract.Count);
        Assert.Equal(Contract.Keys.OrderBy(id => id, StringComparer.Ordinal), Operations.Keys.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OperationIds))]
    public async Task Sends_the_operation_as_the_contract_describes_it(string operationId)
    {
        var operation = Contract[operationId];
        var api = new FakeApi(Answer(operation));
        using var test = new Harness(api);
        await Operations[operationId](test.Client);

        var sent = Assert.Single(api.Requests);
        Assert.Equal(operation.Method, sent.Method);
        var ids = new Queue<string>([Id1, Id2, Id3]);
        var expectedPath = System.Text.RegularExpressions.Regex.Replace(operation.Path, "\\{[^}]+\\}", _ => ids.Dequeue());
        Assert.Equal(expectedPath, sent.Path);

        var query = sent.Query.TrimStart('?').Split(['&'], StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=')[0]).ToHashSet();
        Assert.Subset(operation.Query, query);
        if (operation.Query.Count > 0)
        {
            // Every filter of the contract can be sent.
            Assert.Equal(operation.Query, query);
        }

        switch (operation.BodyType)
        {
            case "application/json":
                Assert.Equal("application/json", sent.Header("Content-Type"));
                JsonDocument.Parse(sent.BodyText).Dispose();
                break;
            case "multipart/form-data":
                Assert.StartsWith("multipart/form-data", sent.Header("Content-Type"));
                break;
            default:
                Assert.True(sent.Body is null || sent.Body.Length == 0, $"{operationId} sends no body");
                break;
        }
        Assert.Equal(operation.Idempotent, sent.Header("Idempotency-Key") is not null);
    }

    [Fact]
    public void Each_resource_method_has_a_WithResponse_twin_with_the_same_parameters()
    {
        var resources = typeof(FlashpiClient).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "Flashpi.Resources" && !type.Name.Contains("WithResponse"))
            .ToList();
        var methods = 0;
        foreach (var resource in resources)
        {
            var view = resource.GetProperty("WithResponse")!.PropertyType;
            foreach (var method in resource.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal)))
            {
                methods++;
                var twin = view.GetMethod(method.Name);
                Assert.True(twin is not null, $"{view.Name}.{method.Name}");
                Assert.Equal(
                    method.GetParameters().Select(parameter => (parameter.Name, parameter.ParameterType.Name, parameter.HasDefaultValue)),
                    twin!.GetParameters().Select(parameter => (parameter.Name, parameter.ParameterType.Name, parameter.HasDefaultValue)));
                Assert.Equal(typeof(System.Threading.CancellationToken), method.GetParameters().Last().ParameterType);
            }
        }
        // The six document issuers share DocumentIssuer<TBody>.
        Assert.Equal(45 - 5, methods);
    }

    private static T Empty<T>() => JsonSerializer.Deserialize("{}", Json.TypeInfo<T>())!;

    private static HttpResponseMessage Answer(Operation operation) => operation.AnswerType switch
    {
        null => Reply.Empty(operation.Status),
        "application/xml" => Reply.Text("<x/>", operation.Status, "application/xml"),
        "application/pdf" => new HttpResponseMessage((System.Net.HttpStatusCode)operation.Status) { Content = new ByteArrayContent([0x25]) },
        _ when operation.Id.EndsWith("List", StringComparison.Ordinal) => Reply.Json(new { data = Array.Empty<object>(), pagination = new { nextCursor = (string?)null, hasMore = false } }, operation.Status),
        _ => Reply.Json("{}", operation.Status),
    };

    private static Dictionary<string, Operation> ReadContract()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "openapi.json")));
        var root = document.RootElement;
        var parameters = root.GetProperty("components").GetProperty("parameters");
        var operations = new Dictionary<string, Operation>();
        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var item in path.Value.EnumerateObject())
            {
                if (item.Name is not ("get" or "post" or "patch" or "put" or "delete"))
                {
                    continue;
                }
                var spec = item.Value;
                var query = new HashSet<string>();
                var idempotent = false;
                var declared = spec.TryGetProperty("parameters", out var list) ? list.EnumerateArray().ToList() : [];
                foreach (var raw in declared)
                {
                    var parameter = raw.TryGetProperty("$ref", out var reference)
                        ? parameters.GetProperty(reference.GetString()!.Split('/').Last())
                        : raw;
                    var location = parameter.GetProperty("in").GetString();
                    var name = parameter.GetProperty("name").GetString()!;
                    if (location == "query")
                    {
                        query.Add(name);
                    }
                    else if (location == "header" && name == "Idempotency-Key")
                    {
                        idempotent = true;
                    }
                }
                string? bodyType = null;
                if (spec.TryGetProperty("requestBody", out var body))
                {
                    bodyType = body.GetProperty("content").EnumerateObject().First().Name;
                }
                var success = spec.GetProperty("responses").EnumerateObject().First(response => response.Name.StartsWith("2", StringComparison.Ordinal));
                string? answerType = success.Value.TryGetProperty("content", out var content) ? content.EnumerateObject().First().Name : null;
                var id = spec.GetProperty("operationId").GetString()!;
                operations[id] = new Operation(id, item.Name.ToUpperInvariant(), path.Name, query, bodyType, int.Parse(success.Name, System.Globalization.CultureInfo.InvariantCulture), answerType, idempotent);
            }
        }
        return operations;
    }
}
