using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Flashpi.Tools.OpenApi;
using Xunit;

namespace Flashpi.Tests.Tools;

public sealed class GeneratorTests
{
    private static string Contract() => File.ReadAllText(Path.Combine(VersionTests.RepositoryRoot(), "openapi", "openapi.json"));

    [Fact]
    public void The_committed_models_match_the_committed_contract()
    {
        var models = File.ReadAllText(Path.Combine(VersionTests.RepositoryRoot(), "src", "Flashpi", "Generated", "Models.g.cs"));
        Assert.Equal(new Generator(Contract()).Generate(), models);
    }

    [Fact]
    public void Generates_the_same_output_twice() =>
        Assert.Equal(new Generator(Contract()).Generate(), new Generator(Contract()).Generate());

    private static string Fails(System.Action<JsonObject> change)
    {
        var document = JsonNode.Parse(Contract())!.AsObject();
        change(document);
        return Assert.Throws<GenerationException>(() => new Generator(document.ToJsonString()).Generate()).Message;
    }

    private static JsonObject Schemas(JsonObject document) => document["components"]!["schemas"]!.AsObject();

    [Fact]
    public void Fails_on_a_construct_it_does_not_know()
    {
        Assert.Contains("unsupported keywords if", Fails(document => Schemas(document)["Pagination"]!["if"] = new JsonObject()));
        Assert.Contains("unknown reference", Fails(document => Schemas(document)["Pagination"]!["properties"]!["hasMore"] = new JsonObject { ["$ref"] = "#/components/schemas/Nope" }));
        Assert.Contains("$ref with sibling keywords", Fails(document => Schemas(document)["Document"]!["properties"]!["ride"] = new JsonObject { ["$ref"] = "#/components/schemas/DocumentRide", ["type"] = "object" }));
        Assert.Contains("several non-null types", Fails(document => Schemas(document)["Pagination"]!["properties"]!["hasMore"] = new JsonObject { ["type"] = new JsonArray("string", "integer") }));
        Assert.Contains("only string enums", Fails(document => Schemas(document)["Pagination"]!["properties"]!["hasMore"] = new JsonObject { ["type"] = "integer", ["enum"] = new JsonArray(1, 2) }));
        Assert.Contains("allOf is only supported with one member", Fails(document => Schemas(document)["Document"]!["properties"]!["ride"] = new JsonObject
        {
            ["allOf"] = new JsonArray(new JsonObject { ["$ref"] = "#/components/schemas/DocumentRide" }, new JsonObject { ["$ref"] = "#/components/schemas/DocumentRide" }),
        }));
        Assert.Contains("Only OpenAPI 3.1", Fails(document => document["openapi"] = "3.0.3"));
    }

    [Fact]
    public void Fails_on_a_new_list_operation_without_a_name_for_its_query()
    {
        var message = Fails(document =>
        {
            var operation = document["paths"]!["/v1/status"]!["get"]!.AsObject();
            operation["operationId"] = "statusList";
            operation["parameters"] = new JsonArray(new JsonObject { ["name"] = "limit", ["in"] = "query", ["schema"] = new JsonObject { ["type"] = "integer" } });
        });
        Assert.Contains("statusList takes a query", message);
    }

    [Fact]
    public void Fails_when_a_webhook_event_loses_a_member_its_base_declares()
    {
        var message = Fails(document => Schemas(document)["EventThin"]!["properties"]!["createdAt"] = new JsonObject { ["type"] = "integer" });
        Assert.Contains("WebhookEvent declares it as a required DateTimeOffset, the document as int", message);
    }

    [Fact]
    public void Fails_on_enum_values_with_the_same_name()
    {
        var message = Fails(document => Schemas(document)["Document"]!["properties"]!["status"]!["enum"]!.AsArray().Add("pending-sri"));
        Assert.Contains("several values are named PendingSri", message);
    }

    [Fact]
    public void Fails_on_a_schema_name_that_is_not_an_identifier()
    {
        var message = Fails(document =>
        {
            var schemas = Schemas(document);
            schemas["Evil{}"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["a"] = new JsonObject { ["type"] = "string" } } };
        });
        Assert.Contains("The schema name Evil{} is not a PascalCase C# identifier", message);
    }

    [Fact]
    public void Keeps_line_breaks_of_the_document_out_of_the_comments()
    {
        var document = JsonNode.Parse(Contract())!.AsObject();
        Schemas(document)["Pagination"]!["properties"]!["hasMore"]!["description"] = "x";
        Schemas(document)["Status"]!["properties"]!["status"]!["enum"]!.AsArray().Add("evil\u2028}class X{");
        var models = new Generator(document.ToJsonString()).Generate();
        Assert.DoesNotContain("\u2028", models);
        Assert.Contains("/// <summary><c>evil }class X{</c>.</summary>", models);
    }

    [Fact]
    public void Fails_on_parameters_declared_for_a_whole_path()
    {
        var message = Fails(document => document["paths"]!["/v1/events"]!["parameters"] = new JsonArray());
        Assert.Contains("Parameters declared for a whole path", message);
    }

    [Fact]
    public void Fails_on_an_event_type_the_discriminator_does_not_map()
    {
        var message = Fails(document => Schemas(document)["CertificateEvent"]!["properties"]!["type"]!["enum"]!.AsArray().Add("certificate.expired"));
        Assert.Contains("CertificateEvent.type allows certificate.expired, which the discriminator does not map to it", message);
    }

    [Fact]
    public void Fails_on_table_entries_that_match_nothing()
    {
        var message = Fails(document =>
        {
            var operation = document["paths"]!["/v1/events"]!["get"]!.AsObject();
            operation["parameters"] = new JsonArray();
        });
        Assert.Contains("Table entries that match nothing in the document: eventsList", message);
    }

    [Fact]
    public void Fails_once_a_whole_number_of_the_table_is_an_integer_in_the_contract()
    {
        var message = Fails(document => Schemas(document)["Document"]!["properties"]!["sequential"]!["type"] = "integer");
        Assert.Contains("Table entries that match nothing in the document: Document.sequential", message);
    }

    [Fact]
    public void Writes_a_late_member_not_required_and_never_null()
    {
        var models = new Generator(Contract()).Generate();
        Assert.Contains("    private readonly IReadOnlyList<CertificateWarning>? _warnings;\n", models);
        Assert.Contains("    public IReadOnlyList<CertificateWarning> Warnings\n    {\n        get => _warnings ?? [];\n        init => _warnings = value;\n    }\n", models);
        // The other required lists stay required.
        Assert.Contains("    public required IReadOnlyList<Certificate> Data { get; init; }\n", models);
    }

    [Fact]
    public void Fails_on_a_late_member_that_is_not_a_required_list_of_an_answer()
    {
        Assert.Contains(
            "Table entries that match nothing in the document: Certificate.warnings",
            Fails(document => Schemas(document)["Certificate"]!["required"]!.AsArray().Remove(
                Schemas(document)["Certificate"]!["required"]!.AsArray().First(name => (string?)name == "warnings"))));
        Assert.Contains(
            "Certificate.warnings: only a list can be a late member (it reads as empty), not string",
            Fails(document => Schemas(document)["Certificate"]!["properties"]!["warnings"] = new JsonObject { ["type"] = "string" }));
        Assert.Contains(
            "Certificate.warnings: a request must send it, so it cannot be a late member",
            Fails(document => document["paths"]!["/v1/status"]!["get"]!["requestBody"] = new JsonObject
            {
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/Certificate" } },
                },
            }));
    }

    [Fact]
    public void Fails_on_a_member_shared_by_name_with_other_values()
    {
        var message = Fails(document => Schemas(document)["Certificate"]!["properties"]!["environment"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray("staging"),
        });
        Assert.Contains("its values differ from those of FlashpiEnvironment, which every .environment shares", message);
    }
}
