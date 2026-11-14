using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Flashpi.Tools.OpenApi;

/// <summary>The document holds something this generator cannot translate faithfully.</summary>
internal sealed class GenerationException(string message) : Exception(message);

/// <summary>
/// Turns the Flashpi OpenAPI document into C# records (<c>src/Flashpi/Generated/Models.g.cs</c>).
/// </summary>
/// <remarks>
/// <para>Every component object schema becomes a <c>sealed partial record</c> whose properties keep
/// the API's names on the wire (<c>[JsonPropertyName]</c>) and are PascalCase in C#; members the schema
/// requires are <c>required</c>. Enums become extensible <c>readonly struct</c>s (a value the SDK does
/// not know yet still reads), the discriminated union <c>Event</c> an abstract record with a converter,
/// and the query of every list operation a <c>...ListParams</c> record. Optional nullable members of
/// request bodies remember whether they were set, so <c>null</c> is sent only when asked for (PATCH).</para>
/// <para>It fails on any construct it does not know, so a change in the document can never be
/// translated silently into a wrong type: extend it, or the tables below, instead.</para>
/// </remarks>
internal sealed partial class Generator
{
    private const string Namespace = "Flashpi.Models";
    private const string GeneratedCode = "[GeneratedCode(\"Flashpi.Tools\", \"1.0\")]";
    private const int LineLength = 100;
    private const string Indent = "    ";
    private const string ExternalBase = "WebhookEvent";

    /// <summary>Component schemas whose C# name is not the schema's.</summary>
    private static readonly Dictionary<string, string> SchemaNames = new(StringComparer.Ordinal)
    {
        // A type cannot have a member named like itself (Status.status).
        ["Status"] = "ApiStatus",
    };

    /// <summary>
    /// Shared names of inline enums, by where they appear (<c>Schema.member</c>, <c>Schema.member[]</c>
    /// for array items, <c>*.member</c> for every schema, <c>operationId?parameter</c>). A shared type
    /// holds the values of all its places. Other inline enums are named after their place
    /// (<c>CertificateStatus</c>).
    /// </summary>
    private static readonly Dictionary<string, string> EnumNames = new(StringComparer.Ordinal)
    {
        ["*.environment"] = "FlashpiEnvironment",
        ["*.payloadMode"] = "PayloadMode",
        ["DocumentEvent.type"] = "EventType",
        ["CertificateEvent.type"] = "EventType",
        ["AccountEvent.type"] = "EventType",
        ["EventThin.type"] = "EventType",
        ["WebhookEndpointVerificationEvent.type"] = "EventType",
        ["WebhookDelivery.eventType"] = "EventType",
        ["WebhookDeliveryDetail.eventType"] = "EventType",
        ["CreateWebhookEndpoint.enabledEvents[]"] = "EventType",
        ["UpdateWebhookEndpoint.enabledEvents[]"] = "EventType",
        ["WebhookEndpoint.enabledEvents[]"] = "EventType",
        ["eventsList?type"] = "EventType",
        ["webhookEndpointsDeliveriesList?eventType"] = "EventType",
        ["Document.status"] = "DocumentStatus",
        ["ProblemDetails.documentStatus"] = "DocumentStatus",
        ["documentsList?status"] = "DocumentStatus",
        ["Document.type"] = "DocumentType",
        ["WaybillSupportingDocument.type"] = "DocumentType",
        ["documentsList?type"] = "DocumentType",
        ["IssuerTaxProfile.rimpeRegime"] = "RimpeRegime",
        ["IssuerTaxProfileInput.rimpeRegime"] = "RimpeRegime",
        ["IssuerTaxProfilePatch.rimpeRegime"] = "RimpeRegime",
        ["DocumentParty.idType"] = "IdType",
        ["WaybillCarrier.idType"] = "IdType",
        ["WithheldSubject.idType"] = "IdType",
        ["WebhookDelivery.status"] = "WebhookDeliveryStatus",
        ["WebhookDeliveryDetail.status"] = "WebhookDeliveryStatus",
        ["webhookEndpointsDeliveriesList?status"] = "WebhookDeliveryStatus",
        ["ProblemDetails.rideStatus"] = "RideStatus",
        ["DocumentRide.status"] = "RideStatus",
        ["DocumentTax.type"] = "TaxType",
        ["DocumentTaxTotal.type"] = "TaxType",
        ["DocumentPayment.method"] = "PaymentMethod",
        ["WithholdingPayment.method"] = "PaymentMethod",
        ["Status.status"] = "ApiStatusKind",
    };

    /// <summary>The summaries of the shared enums, which no single place describes.</summary>
    private static readonly Dictionary<string, string> EnumDocs = new(StringComparer.Ordinal)
    {
        ["FlashpiEnvironment"] =
            "The mode of a key or a resource: `test` (SRI pruebas, fp_test_ keys) or `live` (SRI production, fp_live_ keys).",
        ["PayloadMode"] =
            "What a webhook endpoint receives: `full` events with their `data`, or `thin` ones without it.",
        ["EventType"] =
            "The type of an event or a webhook. New types may appear: keep a default branch. `*` (`All`) subscribes an endpoint to every type.",
        ["DocumentStatus"] = "The status of a document.",
        ["DocumentType"] = "The type of a document.",
        ["RimpeRegime"] =
            "RIMPE regime: `entrepreneur` (CONTRIBUYENTE RÉGIMEN RIMPE) or `popular_business` (CONTRIBUYENTE NEGOCIO POPULAR - RÉGIMEN RIMPE).",
        ["IdType"] = "The type of an identification.",
        ["WebhookDeliveryStatus"] = "The status of a webhook delivery.",
        ["RideStatus"] = "The status of the RIDE (the printable PDF) of a document.",
        ["TaxType"] = "A tax: `vat` (IVA) or `ice` (ICE).",
        ["PaymentMethod"] = "A payment method.",
        ["ApiStatusKind"] = "Overall status of Flashpi.",
    };

    /// <summary>The query records of the list operations, by operationId.</summary>
    private static readonly Dictionary<string, string> QueryNames = new(StringComparer.Ordinal)
    {
        ["issuersList"] = "IssuerListParams",
        ["establishmentsList"] = "EstablishmentListParams",
        ["emissionPointsList"] = "EmissionPointListParams",
        ["certificatesList"] = "CertificateListParams",
        ["documentsList"] = "DocumentListParams",
        ["webhookEndpointsList"] = "WebhookEndpointListParams",
        ["webhookEndpointsDeliveriesList"] = "WebhookDeliveryListParams",
        ["eventsList"] = "EventListParams",
    };

    /// <summary>Members that hold a secret: the record's <c>ToString()</c> hides them.</summary>
    private static readonly HashSet<string> SecretMembers = ["WebhookEndpointSecret.secret"];

    /// <summary>C# names of enum values that have no letters or digits.</summary>
    private static readonly Dictionary<string, string> MemberNames = new(StringComparer.Ordinal)
    {
        ["*"] = "All",
    };

    /// <summary>
    /// The schemas that derive from the hand-written <c>WebhookEvent</c> (everything a webhook
    /// endpoint receives), and the members it declares for them, with their C# types.
    /// </summary>
    private static readonly string[] WebhookEventSchemas = ["Event", "EventThin", "WebhookEndpointVerificationEvent"];

    private static readonly (string Json, string Type)[] WebhookEventMembers =
    [
        ("id", "string"),
        ("type", "EventType"),
        ("environment", "FlashpiEnvironment"),
        ("createdAt", "DateTimeOffset"),
    ];

    /// <summary>Keywords that only constrain values or document them: records cannot express them.</summary>
    private static readonly HashSet<string> AnnotationKeywords =
    [
        "description", "example", "examples", "format", "pattern", "minLength", "maxLength", "minimum",
        "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minItems", "maxItems",
        "uniqueItems", "minProperties", "maxProperties", "default", "title", "deprecated",
    ];

    private static readonly HashSet<string> StructureKeywords =
    [
        "type", "properties", "required", "items", "enum", "const", "$ref", "allOf", "anyOf", "oneOf",
        "discriminator", "additionalProperties", "not",
    ];

    private static readonly string[] NotWithProperties = ["allOf", "anyOf", "oneOf", "enum", "const", "items"];

    private static readonly string[] Combinators = ["anyOf", "oneOf"];

    private static readonly string[] ReferenceCombinators = ["allOf", "anyOf"];

    private static readonly JsonSerializerOptions LiteralOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Members every enum struct declares, which no value may be named.</summary>
    private static readonly HashSet<string> EnumReserved = ["Value", "Equals", "GetHashCode", "ToString", "GetType", "Converter"];

    private readonly JsonObject _document;
    private readonly JsonObject _schemas;
    private readonly JsonObject _parameters;
    private readonly Dictionary<string, TypeModel> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _requestSchemas = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedPlaces = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedSecrets = new(StringComparer.Ordinal);

    public Generator(string contract)
    {
        _document = AsObject(JsonNode.Parse(contract), "document");
        if (_document["openapi"]?.GetValue<string>() is not { } version || !version.StartsWith("3.1", StringComparison.Ordinal))
        {
            throw new GenerationException("Only OpenAPI 3.1 documents are supported");
        }
        var components = AsObject(_document["components"] ?? new JsonObject(), "components");
        _schemas = AsObject(components["schemas"] ?? new JsonObject(), "components.schemas");
        _parameters = AsObject(components["parameters"] ?? new JsonObject(), "components.parameters");
    }

    /// <summary>The C# source of the models.</summary>
    public string Generate()
    {
        FindRequestSchemas();
        FindBases();
        foreach (var name in _schemas.Select(entry => entry.Key).Order(StringComparer.Ordinal))
        {
            DefineComponent(name, AsObject(_schemas[name], name));
        }
        DefineQueries();
        CheckWebhookEventMembers();
        CheckTablesAreUsed();
        return Emit();
    }

    /// <summary>Every entry of the tables names something of the document: a stale one fails.</summary>
    private void CheckTablesAreUsed()
    {
        var stale = SchemaNames.Keys.Where(name => !_schemas.ContainsKey(name))
            .Concat(QueryNames.Where(entry => !_types.ContainsKey(entry.Value)).Select(entry => entry.Key))
            .Concat(EnumNames.Where(entry => !entry.Key.StartsWith('*') && !_usedPlaces.Contains(entry.Key)).Select(entry => entry.Key))
            .Concat(SecretMembers.Where(member => !_usedSecrets.Contains(member)))
            .ToList();
        if (stale.Count > 0)
        {
            throw new GenerationException($"Table entries that match nothing in the document: {string.Join(", ", stale)}");
        }
    }

    // Names.

    private static string TypeName(string schema)
    {
        var name = SchemaNames.GetValueOrDefault(schema, schema);
        // A schema name becomes C# source: only a plain identifier is accepted.
        if (!JsonNamePattern().IsMatch(name) || !char.IsAsciiLetterUpper(name[0]))
        {
            throw new GenerationException($"The schema name {schema} is not a PascalCase C# identifier");
        }
        return name;
    }

    private static string UpperFirst(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string PropertyName(string json, string where)
    {
        if (!JsonNamePattern().IsMatch(json))
        {
            throw new GenerationException($"{where}: the member {json} is not a valid C# name");
        }
        return UpperFirst(json);
    }

    private static string MemberName(string value, string where)
    {
        if (MemberNames.TryGetValue(value, out var known))
        {
            return known;
        }
        var parts = NonAlphanumeric().Split(value).Where(part => part.Length > 0).Select(UpperFirst);
        var name = string.Concat(parts);
        if (name.Length == 0)
        {
            throw new GenerationException($"{where}: no C# name for the enum value {value}: add it to MemberNames");
        }
        return char.IsAsciiDigit(name[0]) ? "Value" + name : name;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex JsonNamePattern();

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    // Which schemas are request bodies (their optional nullable members remember being set), and
    // which derive from which.

    private void FindRequestSchemas()
    {
        foreach (var (_, item) in AsObject(_document["paths"], "paths"))
        {
            if (AsObject(item, "path")["parameters"] is not null)
            {
                throw new GenerationException("Parameters declared for a whole path are not supported: declare them on each operation");
            }
            foreach (var (method, operation) in AsObject(item, "path"))
            {
                if (operation is JsonObject body && body["requestBody"] is JsonNode requestBody && IsMethod(method))
                {
                    CollectReferences(requestBody, _requestSchemas);
                }
            }
        }
    }

    private void CollectReferences(JsonNode? node, HashSet<string> found)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["$ref"]?.GetValue<string>() is { } target && target.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                {
                    var name = target["#/components/schemas/".Length..];
                    if (found.Add(name) && _schemas[name] is JsonNode schema)
                    {
                        CollectReferences(schema, found);
                    }
                }
                foreach (var (_, value) in obj)
                {
                    CollectReferences(value, found);
                }
                break;
            case JsonArray array:
                foreach (var value in array)
                {
                    CollectReferences(value, found);
                }
                break;
            default:
                break;
        }
    }

    private void FindBases()
    {
        foreach (var name in WebhookEventSchemas)
        {
            if (_schemas[name] is null)
            {
                throw new GenerationException($"{name} is gone from the document: update WebhookEventSchemas");
            }
            _bases[TypeName(name)] = ExternalBase;
        }
        foreach (var (name, node) in _schemas)
        {
            if (node is JsonObject schema && schema["oneOf"] is JsonArray members && schema["discriminator"] is not null)
            {
                foreach (var member in members)
                {
                    var reference = AsObject(member, name)["$ref"]?.GetValue<string>()
                        ?? throw new GenerationException($"{name}: a discriminated union must list references only");
                    _bases[TypeName(reference["#/components/schemas/".Length..])] = TypeName(name);
                }
            }
        }
    }

    /// <summary>The members a record inherits from its bases (only <c>WebhookEvent</c> declares any).</summary>
    private bool DerivesFromWebhookEvent(string name)
    {
        for (var current = name; _bases.TryGetValue(current, out var parent); current = parent)
        {
            if (parent == ExternalBase)
            {
                return true;
            }
        }
        return false;
    }

    // Definitions.

    private void Define(TypeModel model)
    {
        if (!_types.TryAdd(model.Name, model))
        {
            throw new GenerationException($"Two definitions are named {model.Name}");
        }
    }

    private void DefineComponent(string schemaName, JsonObject schema)
    {
        var where = $"components.schemas.{schemaName}";
        CheckKeywords(schema, where);
        var name = TypeName(schemaName);
        if (schema["oneOf"] is not null && schema["discriminator"] is not null)
        {
            DefineUnion(name, schema, where);
        }
        else if (schema["properties"] is not null)
        {
            DefineRecord(name, schemaName, schema, where, _requestSchemas.Contains(schemaName));
        }
        else if (schema["enum"] is not null && TypesOf(schema, where).SequenceEqual(["string"]))
        {
            DefineEnum(name, null, StringValues(schema, where), Describe(schema), where);
        }
        else
        {
            throw new GenerationException($"{where}: only objects, string enums and discriminated unions are supported");
        }
    }

    private void DefineRecord(string name, string placeKey, JsonObject schema, string where, bool request)
    {
        var unexpected = NotWithProperties.Where(key => schema[key] is not null);
        if (unexpected.Any() || !(TypesOf(schema, where) is [] or ["object"]))
        {
            throw new GenerationException($"{where}: properties mixed with other types");
        }
        if (schema["not"] is JsonNode not)
        {
            CheckNot(schema, AsObject(not, $"{where}.not"), where);
        }
        var record = new RecordModel(name, Describe(schema)) { Base = _bases.GetValueOrDefault(name), IsRequest = request };
        switch (schema["additionalProperties"])
        {
            case null:
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.False:
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.True:
                record.HasExtensionData = true;
                break;
            default:
                throw new GenerationException($"{where}: typed members with typed additionalProperties");
        }
        Define(record);
        var properties = AsObject(schema["properties"], $"{where}.properties");
        var required = StringList(schema["required"], where).ToHashSet(StringComparer.Ordinal);
        var missing = required.Where(key => !properties.ContainsKey(key)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new GenerationException($"{where}: required members without a schema: {string.Join(", ", missing)}");
        }
        var inherited = DerivesFromWebhookEvent(name);
        foreach (var (key, value) in properties)
        {
            var member = AsObject(value, $"{where}.{key}");
            var propertyName = PropertyName(key, where);
            if (propertyName == name)
            {
                throw new GenerationException($"{where}: the member {key} is named like its type: rename the type in SchemaNames");
            }
            var type = Render(member, $"{name}{propertyName}", $"{where}.{key}", $"{placeKey}.{key}", request);
            var isRequired = required.Contains(key);
            if (inherited && WebhookEventMembers.Any(entry => entry.Json == key))
            {
                var expected = WebhookEventMembers.First(entry => entry.Json == key).Type;
                if (type.Name != expected || type.Nullable || !isRequired)
                {
                    throw new GenerationException(
                        $"{where}.{key}: {ExternalBase} declares it as a required {expected}, the document as {type.Expression}");
                }
                record.Inherited.Add(key);
                continue;
            }
            var tracked = request && !isRequired && type.Nullable;
            var secret = SecretMembers.Contains($"{placeKey}.{key}");
            if (secret)
            {
                _usedSecrets.Add($"{placeKey}.{key}");
                record.HasSecret = true;
            }
            var doc = Describe(member);
            record.Properties.Add(new PropertyModel(key, propertyName, type, isRequired, tracked, doc.Length > 0 ? doc : ReferencedDoc(member)) { Secret = secret });
        }
        record.ListItem = ListItemOf(record);
    }

    private static string? ListItemOf(RecordModel record)
    {
        if (record.Properties is [var data, var pagination]
            && data is { JsonName: "data", Required: true, Type.Nullable: false }
            && data.Type.Name.StartsWith("IReadOnlyList<", StringComparison.Ordinal)
            && pagination is { JsonName: "pagination", Required: true, Type.Name: "Pagination", Type.Nullable: false })
        {
            return data.Type.Name["IReadOnlyList<".Length..^1];
        }
        return null;
    }

    private void CheckWebhookEventMembers()
    {
        foreach (var model in _types.Values.OfType<RecordModel>().Where(model => DerivesFromWebhookEvent(model.Name)))
        {
            var missing = WebhookEventMembers.Select(entry => entry.Json).Where(json => !model.Inherited.Contains(json)).ToList();
            if (missing.Count > 0)
            {
                throw new GenerationException($"{model.Name} has no {string.Join(", ", missing)}, which {ExternalBase} declares");
            }
        }
    }

    private static void CheckNot(JsonObject schema, JsonObject forbidden, string where)
    {
        // `not: {required: [...]}` forbids members: the record leaves them undeclared.
        if (forbidden.Count != 1 || forbidden["required"] is null)
        {
            throw new GenerationException($"{where}: `not` is only supported as not.required");
        }
        var properties = AsObject(schema["properties"] ?? new JsonObject(), where);
        foreach (var key in StringList(forbidden["required"], $"{where}.not.required"))
        {
            if (properties.ContainsKey(key))
            {
                throw new GenerationException($"{where}: {key} is both declared and forbidden");
            }
        }
    }

    private void DefineUnion(string name, JsonObject schema, string where)
    {
        var structure = schema.Select(entry => entry.Key).Where(key => StructureKeywords.Contains(key) && key is not "oneOf" and not "discriminator");
        if (structure.Any())
        {
            throw new GenerationException($"{where}: oneOf with {string.Join(", ", structure)}");
        }
        var discriminator = AsObject(schema["discriminator"], $"{where}.discriminator");
        var property = discriminator["propertyName"]?.GetValue<string>()
            ?? throw new GenerationException($"{where}: the discriminator has no propertyName");
        if (property != "type" || name != "Event")
        {
            throw new GenerationException($"{where}: only the Event union, on `type`, is supported");
        }
        var members = AsArray(schema["oneOf"], where)
            .Select(member => TypeName(AsObject(member, where)["$ref"]!.GetValue<string>()["#/components/schemas/".Length..]))
            .ToList();
        var union = new UnionModel(name, Describe(schema), property, $"Unknown{name}");
        foreach (var (value, target) in AsObject(discriminator["mapping"], $"{where}.discriminator.mapping"))
        {
            var targetName = TypeName(target!.GetValue<string>()["#/components/schemas/".Length..]);
            if (!members.Contains(targetName))
            {
                throw new GenerationException($"{where}: mapping {value} points outside the union");
            }
            var memberSchema = AsObject(_schemas[target.GetValue<string>()["#/components/schemas/".Length..]], targetName);
            var discriminant = AsObject(AsObject(memberSchema["properties"], targetName)[property], $"{targetName}.{property}");
            var allowed = discriminant["enum"] is JsonArray values
                ? values.Select(entry => entry?.GetValue<string>()).ToList()
                : [discriminant["const"]?.GetValue<string>()];
            if (!allowed.Contains(value))
            {
                throw new GenerationException($"{where}: {targetName}.{property} does not allow {value}");
            }
            union.Mapping.Add((value, targetName));
        }
        foreach (var member in members.Where(member => union.Mapping.All(entry => entry.Member != member)))
        {
            throw new GenerationException($"{where}: no mapping value selects {member}");
        }
        // Every value a member allows must select it, or its events would read as unknown.
        foreach (var member in members)
        {
            var schemaName = _schemas.First(entry => TypeName(entry.Key) == member).Key;
            var discriminant = AsObject(AsObject(AsObject(_schemas[schemaName], member)["properties"], member)[property], $"{member}.{property}");
            var allowed = discriminant["enum"] is JsonArray values
                ? values.Select(entry => entry!.GetValue<string>())
                : [discriminant["const"]!.GetValue<string>()];
            foreach (var value in allowed.Where(value => !union.Mapping.Contains((value, member))))
            {
                throw new GenerationException($"{where}: {member}.{property} allows {value}, which the discriminator does not map to it");
            }
        }
        Define(union);
        // An event of a type this SDK does not know yet keeps its other members as JSON.
        Define(new RecordModel(
            union.UnknownName,
            $"An {name} of a type this version of the SDK does not know yet: its members other than those of `{ExternalBase}` are in `AdditionalProperties`.")
        {
            Base = name,
            HasExtensionData = true,
            Inherited = [.. WebhookEventMembers.Select(entry => entry.Json)],
        });
    }

    private TypeRef DefineEnum(string hint, string? place, List<string> values, string doc, string where)
    {
        var shared = place is null ? null : SharedEnumName(place);
        if (shared is not null)
        {
            _usedPlaces.Add(place!);
        }
        var name = shared ?? hint;
        if (_types.TryGetValue(name, out var existing))
        {
            if (shared is null || existing is not EnumModel known)
            {
                throw new GenerationException($"{where}: two definitions are named {name}");
            }
            // A place shared by member name (*.environment) must hold the same values: a new
            // member of that name with other values is something else.
            if (place is not null && !EnumNames.ContainsKey(place) && !known.Values.ToHashSet(StringComparer.Ordinal).SetEquals(values))
            {
                throw new GenerationException(
                    $"{where}: its values differ from those of {name}, which every {place[place.IndexOf('.', StringComparison.Ordinal)..]} shares: name it in EnumNames");
            }
            known.Values.AddRange(values.Where(value => !known.Values.Contains(value)));
        }
        else
        {
            var model = new EnumModel(name, shared is null ? doc : EnumDocs.GetValueOrDefault(shared, doc));
            model.Values.AddRange(values.Distinct());
            Define(model);
        }
        return new TypeRef(name, IsValueType: true, Nullable: false);
    }

    private static string? SharedEnumName(string place)
    {
        if (EnumNames.TryGetValue(place, out var name))
        {
            return name;
        }
        var member = place.Contains('.', StringComparison.Ordinal) ? place[place.IndexOf('.', StringComparison.Ordinal)..] : null;
        return member is null ? null : EnumNames.GetValueOrDefault("*" + member);
    }

    private void DefineQueries()
    {
        foreach (var (path, item) in AsObject(_document["paths"], "paths"))
        {
            foreach (var (method, node) in AsObject(item, path))
            {
                if (!IsMethod(method))
                {
                    continue;
                }
                var operation = AsObject(node, $"{method} {path}");
                var operationId = operation["operationId"]?.GetValue<string>()
                    ?? throw new GenerationException($"{method} {path}: no operationId");
                var query = (operation["parameters"] as JsonArray ?? [])
                    .Select(parameter => Parameter(AsObject(parameter, operationId), operationId))
                    .Where(parameter => parameter["in"]?.GetValue<string>() == "query")
                    .ToList();
                if (query.Count == 0)
                {
                    continue;
                }
                if (!QueryNames.TryGetValue(operationId, out var name))
                {
                    throw new GenerationException($"{operationId} takes a query: name its record in QueryNames");
                }
                var record = new RecordModel(name, $"The query of `{operationId}` ({method.ToUpperInvariant()} {path}).") { IsQuery = true };
                Define(record);
                foreach (var parameter in query)
                {
                    var key = parameter["name"]!.GetValue<string>();
                    var where = $"{operationId}.{key}";
                    var schema = AsObject(parameter["schema"] ?? new JsonObject(), where);
                    var type = Render(schema, $"{name}{PropertyName(key, where)}", where, $"{operationId}?{key}", request: false);
                    if (type.Name is not ("string" or "int" or "long" or "double" or "bool") && _types.GetValueOrDefault(type.Name) is not EnumModel)
                    {
                        throw new GenerationException($"{where}: only scalar query parameters are supported");
                    }
                    var required = parameter["required"]?.GetValue<bool>() == true;
                    var doc = JoinSentences(Describe(parameter), Describe(schema));
                    record.Properties.Add(new PropertyModel(key, PropertyName(key, where), type, required, false, doc));
                }
            }
        }
    }

    private JsonObject Parameter(JsonObject parameter, string where)
    {
        if (parameter["$ref"]?.GetValue<string>() is not { } target)
        {
            return parameter;
        }
        const string prefix = "#/components/parameters/";
        if (!target.StartsWith(prefix, StringComparison.Ordinal) || _parameters[target[prefix.Length..]] is not JsonObject resolved)
        {
            throw new GenerationException($"{where}: unknown parameter {target}");
        }
        return resolved;
    }

    // Types of members.

    private TypeRef Render(JsonObject schema, string hint, string where, string place, bool request)
    {
        CheckKeywords(schema, where);
        if (schema["$ref"] is not null)
        {
            return new TypeRef(Reference(schema, where), IsValueType: IsEnumSchema(schema), Nullable: false);
        }
        if (schema["allOf"] is JsonArray allOf)
        {
            CheckCombination(schema, "allOf", where);
            if (allOf.Count != 1)
            {
                throw new GenerationException($"{where}: allOf is only supported with one member");
            }
            return Render(AsObject(allOf[0], where), hint, $"{where}.allOf[0]", place, request);
        }
        foreach (var combinator in Combinators)
        {
            if (schema[combinator] is JsonArray members)
            {
                CheckCombination(schema, combinator, where);
                var options = members.Select(member => AsObject(member, where)).ToList();
                var nulls = options.Count(option => TypesOf(option, where) is ["null"] && option.Count == 1);
                if (options.Count != 2 || nulls != 1)
                {
                    throw new GenerationException($"{where}: {combinator} is only supported as one schema or null");
                }
                var inner = Render(options.First(option => !(TypesOf(option, where) is ["null"] && option.Count == 1)), hint, where, place, request);
                return inner with { Nullable = true };
            }
        }
        var types = TypesOf(schema, where);
        var nullable = types.Remove("null");
        if (schema["not"] is not null && !types.SequenceEqual(["object"]))
        {
            throw new GenerationException($"{where}: `not` is only supported on objects");
        }
        if (schema["const"] is JsonNode constant)
        {
            if (constant.GetValueKind() != JsonValueKind.String || types is not ([] or ["string"]))
            {
                throw new GenerationException($"{where}: only string constants are supported");
            }
            return DefineEnum(hint, place, [constant.GetValue<string>()], Describe(schema), where) with { Nullable = nullable };
        }
        if (schema["enum"] is JsonArray enumValues)
        {
            if (types is not ["string"])
            {
                throw new GenerationException($"{where}: only string enums are supported");
            }
            var values = enumValues.Where(value => value is not null).Select(value => value!.GetValue<string>()).ToList();
            if (values.Count == 0)
            {
                throw new GenerationException($"{where}: an enum with no values");
            }
            nullable |= enumValues.Any(value => value is null);
            return DefineEnum(hint, place, values, Describe(schema), where) with { Nullable = nullable };
        }
        if (types.Count > 1)
        {
            throw new GenerationException($"{where}: several non-null types");
        }
        var kind = types.Count == 1 ? types[0] : null;
        return RenderKind(schema, kind, hint, where, place, request) with { Nullable = nullable };
    }

    private TypeRef RenderKind(JsonObject schema, string? kind, string hint, string where, string place, bool request)
    {
        var format = schema["format"]?.GetValue<string>();
        switch (kind)
        {
            case "string" when format == "date-time":
                return new TypeRef("DateTimeOffset", IsValueType: true, Nullable: false);
            case "string" when format == "binary":
                throw new GenerationException($"{where}: binary strings are only supported in multipart bodies");
            case "string":
                return new TypeRef("string", IsValueType: false, Nullable: false);
            case "integer":
                return new TypeRef(format == "int64" ? "long" : "int", IsValueType: true, Nullable: false);
            case "number":
                return new TypeRef("double", IsValueType: true, Nullable: false);
            case "boolean":
                return new TypeRef("bool", IsValueType: true, Nullable: false);
            case "array":
                var items = AsObject(schema["items"] ?? new JsonObject(), $"{where}.items");
                var item = Render(items, $"{hint}Item", $"{where}.items", $"{place}[]", request);
                return new TypeRef($"IReadOnlyList<{item.Expression}>", IsValueType: false, Nullable: false);
            case "object":
            case null when schema["properties"] is not null:
                return RenderObject(schema, hint, where, place, request);
            case null when schema.All(entry => AnnotationKeywords.Contains(entry.Key)):
                return new TypeRef("JsonElement", IsValueType: true, Nullable: false);
            default:
                throw new GenerationException($"{where}: unsupported type {kind}");
        }
    }

    private TypeRef RenderObject(JsonObject schema, string hint, string where, string place, bool request)
    {
        if (schema["properties"] is not null)
        {
            DefineRecord(hint, hint, schema, where, request);
            return new TypeRef(hint, IsValueType: false, Nullable: false);
        }
        if (schema["additionalProperties"] is JsonObject values)
        {
            var value = Render(values, $"{hint}Value", $"{where}.additionalProperties", $"{place}{{}}", request);
            return new TypeRef($"IReadOnlyDictionary<string, {value.Expression}>", IsValueType: false, Nullable: false);
        }
        return new TypeRef("IReadOnlyDictionary<string, JsonElement>", IsValueType: false, Nullable: false);
    }

    private string Reference(JsonObject schema, string where)
    {
        const string prefix = "#/components/schemas/";
        var target = schema["$ref"]!.GetValue<string>();
        if (!target.StartsWith(prefix, StringComparison.Ordinal) || !_schemas.ContainsKey(target[prefix.Length..]))
        {
            throw new GenerationException($"{where}: unknown reference {target}");
        }
        if (schema.Count(entry => !AnnotationKeywords.Contains(entry.Key)) > 1)
        {
            throw new GenerationException($"{where}: $ref with sibling keywords");
        }
        return TypeName(target[prefix.Length..]);
    }

    private bool IsEnumSchema(JsonObject reference)
    {
        var target = AsObject(_schemas[reference["$ref"]!.GetValue<string>()["#/components/schemas/".Length..]], "reference");
        return target["enum"] is not null && target["properties"] is null;
    }

    private static void CheckCombination(JsonObject schema, string combinator, string where)
    {
        var structure = schema.Select(entry => entry.Key).Where(key => StructureKeywords.Contains(key) && key != combinator).ToList();
        if (structure.Count > 0)
        {
            throw new GenerationException($"{where}: {combinator} with {string.Join(", ", structure)}");
        }
    }

    private static void CheckKeywords(JsonObject schema, string where)
    {
        var unknown = schema.Select(entry => entry.Key)
            .Where(key => !AnnotationKeywords.Contains(key) && !StructureKeywords.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (unknown.Count > 0)
        {
            throw new GenerationException($"{where}: unsupported keywords {string.Join(", ", unknown)}");
        }
    }

    private static List<string> TypesOf(JsonObject schema, string where) => schema["type"] switch
    {
        null => [],
        JsonValue value => [value.GetValue<string>()],
        JsonArray array => [.. array.Select(item => item?.GetValue<string>() ?? throw new GenerationException($"{where}: a null type"))],
        _ => throw new GenerationException($"{where}: type must be a string or a list"),
    };

    private static List<string> StringValues(JsonObject schema, string where) =>
        [.. AsArray(schema["enum"], where).Select(value => value?.GetValue<string>() ?? throw new GenerationException($"{where}: a null enum value"))];

    private static IEnumerable<string> StringList(JsonNode? node, string where) =>
        node is null ? [] : AsArray(node, where).Select(value => value!.GetValue<string>());

    private static bool IsMethod(string method) => method is "get" or "post" or "put" or "patch" or "delete";

    private static JsonObject AsObject(JsonNode? node, string where) =>
        node as JsonObject ?? throw new GenerationException($"{where}: expected a schema object");

    private static JsonArray AsArray(JsonNode? node, string where) =>
        node as JsonArray ?? throw new GenerationException($"{where}: expected a list");

    // Documentation.

    private static string Describe(JsonObject schema)
    {
        var parts = new List<string>();
        if (schema["description"]?.GetValue<string>() is { } description && !string.IsNullOrWhiteSpace(description))
        {
            parts.Add(string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        }
        if (schema["format"]?.GetValue<string>() is { } format && format is not ("binary" or "password" or "date-time"))
        {
            parts.Add($"Format: {format}.");
        }
        if (schema["default"] is JsonNode value)
        {
            parts.Add($"Default: `{value.ToJsonString(LiteralOptions)}`.");
        }
        return string.Join(' ', parts);
    }

    /// <summary>The description of the schema a member refers to, for members that have none.</summary>
    private string ReferencedDoc(JsonObject member)
    {
        var target = member["$ref"]?.GetValue<string>();
        foreach (var combinator in ReferenceCombinators)
        {
            if (member[combinator] is JsonArray options)
            {
                target ??= options.OfType<JsonObject>().Select(option => option["$ref"]?.GetValue<string>()).FirstOrDefault(found => found is not null);
            }
        }
        return target is not null && _schemas[target["#/components/schemas/".Length..]] is JsonObject schema ? Describe(schema) : "";
    }

    private static string JoinSentences(string first, string second) =>
        string.Join(' ', new[] { first, second }.Where(part => part.Length > 0));

    // Output.

    private string Emit()
    {
        var output = new StringBuilder();
        output.Append("""
            // <auto-generated/>
            // GENERATED by tools/Flashpi.Tools from openapi/openapi.json. Do not edit by hand: run
            // `dotnet run --project tools/Flashpi.Tools -- openapi generate` (or `openapi sync`).
            #nullable enable

            using System;
            using System.CodeDom.Compiler;
            using System.Collections.Generic;
            using System.Text;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using Flashpi.Internal;

            namespace Flashpi.Models;

            """);
        foreach (var model in _types.Values.OrderBy(model => model.Name, StringComparer.Ordinal))
        {
            output.Append('\n');
            switch (model)
            {
                case RecordModel record:
                    EmitRecord(output, record);
                    break;
                case EnumModel enumeration:
                    EmitEnum(output, enumeration);
                    break;
                case UnionModel union:
                    EmitUnion(output, union);
                    break;
                default:
                    throw new GenerationException($"{model.Name}: unknown model");
            }
        }
        output.Append('\n');
        EmitContext(output);
        return output.ToString().ReplaceLineEndings("\n");
    }

    private static void EmitRecord(StringBuilder output, RecordModel record)
    {
        AppendDoc(output, record.Doc.Length > 0 ? record.Doc : $"The `{record.Name}` object of the API.", "");
        output.Append(GeneratedCode).Append('\n');
        var bases = new List<string>();
        if (record.Base is not null)
        {
            bases.Add(record.Base);
        }
        if (record.ListItem is not null)
        {
            bases.Add($"IListBody<{record.ListItem}>");
        }
        if (record.Properties.Any(property => property.TriState))
        {
            bases.Add("IPresenceTracked");
        }
        output.Append("public sealed partial record ").Append(record.Name);
        if (bases.Count > 0)
        {
            output.Append(" : ").Append(string.Join(", ", bases));
        }
        output.Append("\n{\n");
        var first = true;
        foreach (var property in record.Properties)
        {
            if (!first)
            {
                output.Append('\n');
            }
            first = false;
            EmitProperty(output, record, property);
        }
        if (record.HasExtensionData)
        {
            if (!first)
            {
                output.Append('\n');
            }
            first = false;
            AppendDoc(output, "Members of the answer that this version of the SDK does not declare, as JSON.", Indent);
            output.Append(Indent).Append("[JsonExtensionData]\n");
            // A settable member: System.Text.Json cannot fill extension data through a constructor,
            // which is how it sets init-only members.
            output.Append(Indent).Append("public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }\n");
        }
        var tracked = record.Properties.Where(property => property.TriState).ToList();
        if (tracked.Count > 0)
        {
            output.Append('\n');
            output.Append(Indent).Append("bool IPresenceTracked.IsSet(string jsonName) => jsonName switch\n");
            output.Append(Indent).Append("{\n");
            foreach (var property in tracked)
            {
                output.Append(Indent).Append(Indent).Append($"\"{property.JsonName}\" => _{property.JsonName}IsSet,\n");
            }
            output.Append(Indent).Append(Indent).Append("_ => true,\n");
            output.Append(Indent).Append("};\n");
        }
        if (record.HasSecret)
        {
            if (record.Base is not null || record.HasExtensionData)
            {
                throw new GenerationException($"{record.Name}: secrets are only supported on records without a base or extra members");
            }
            // The records' ToString() lists their members: never a secret's value.
            output.Append('\n');
            output.Append(Indent).Append("private bool PrintMembers(StringBuilder builder)\n");
            output.Append(Indent).Append("{\n");
            var separator = "";
            foreach (var property in record.Properties)
            {
                var value = property.Secret ? "\"<hidden>\"" : $"(object?){property.Name}";
                output.Append(Indent).Append(Indent).Append($"builder.Append(\"{separator}{property.Name} = \").Append({value});\n");
                separator = ", ";
            }
            output.Append(Indent).Append(Indent).Append("return true;\n");
            output.Append(Indent).Append("}\n");
        }
        output.Append("}\n");
    }

    private static void EmitProperty(StringBuilder output, RecordModel record, PropertyModel property)
    {
        var type = property.Type with { Nullable = property.Type.Nullable || !property.Required };
        var doc = property.Doc.Length > 0 ? property.Doc : $"The `{property.JsonName}` member.";
        if (property.TriState)
        {
            doc += " Left out of the request unless set: set it to null to send null.";
            output.Append(Indent).Append($"private {type.Expression} _{property.JsonName};\n");
            output.Append(Indent).Append($"private bool _{property.JsonName}IsSet;\n\n");
        }
        AppendDoc(output, doc, Indent);
        output.Append(Indent).Append($"[JsonPropertyName(\"{property.JsonName}\")]\n");
        if (property.Required && type.Nullable && record.IsRequest)
        {
            // Sent even when null, unlike the other members of a request.
            output.Append(Indent).Append("[JsonIgnore(Condition = JsonIgnoreCondition.Never)]\n");
        }
        var required = property.Required && !record.IsQuery ? "required " : "";
        if (property.TriState)
        {
            output.Append(Indent).Append($"public {type.Expression} {property.Name}\n");
            output.Append(Indent).Append("{\n");
            output.Append(Indent).Append(Indent).Append($"get => _{property.JsonName};\n");
            output.Append(Indent).Append(Indent).Append("init\n");
            output.Append(Indent).Append(Indent).Append("{\n");
            output.Append(Indent).Append(Indent).Append(Indent).Append($"_{property.JsonName} = value;\n");
            output.Append(Indent).Append(Indent).Append(Indent).Append($"_{property.JsonName}IsSet = true;\n");
            output.Append(Indent).Append(Indent).Append("}\n");
            output.Append(Indent).Append("}\n");
            return;
        }
        output.Append(Indent).Append($"public {required}{type.Expression} {property.Name} {{ get; init; }}\n");
    }

    private static void EmitEnum(StringBuilder output, EnumModel model)
    {
        var name = model.Name;
        var members = model.Values.Select(value => (Value: value, Member: MemberName(value, name))).ToList();
        foreach (var duplicate in members.GroupBy(member => member.Member).Where(group => group.Count() > 1))
        {
            throw new GenerationException($"{name}: several values are named {duplicate.Key}");
        }
        foreach (var (_, member) in members.Where(member => EnumReserved.Contains(member.Member) || member.Member == name))
        {
            throw new GenerationException($"{name}: a value would be named {member}: add it to MemberNames");
        }
        var doc = model.Doc.Length > 0 ? model.Doc : $"The `{name}` values of the API.";
        AppendDoc(output, $"{doc} Values this version of the SDK does not know yet are kept as they are: compare with the static members, or read `Value`.", "");
        output.Append(GeneratedCode).Append('\n');
        output.Append($"[JsonConverter(typeof({name}.Converter))]\n");
        output.Append($"public readonly partial struct {name} : IEquatable<{name}>, IComparable<{name}>\n{{\n");
        output.Append(Indent).Append("private readonly string? _value;\n\n");
        AppendDoc(output, "A value as the API writes it, also one this version of the SDK does not know yet.", Indent);
        output.Append(Indent).Append($"public {name}(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));\n");
        foreach (var (value, member) in members)
        {
            output.Append('\n');
            AppendDoc(output, $"`{value}`.", Indent);
            output.Append(Indent).Append($"public static {name} {member} {{ get; }} = new({Literal(value)});\n");
        }
        output.Append('\n');
        AppendDoc(output, "The value as the API writes it (empty for the default value).", Indent);
        output.Append(Indent).Append("public string Value => _value ?? string.Empty;\n\n");
        AppendDoc(output, "Whether both values are the same.", Indent);
        output.Append(Indent).Append($"public static bool operator ==({name} left, {name} right) => left.Equals(right);\n\n");
        AppendDoc(output, "Whether the values differ.", Indent);
        output.Append(Indent).Append($"public static bool operator !=({name} left, {name} right) => !left.Equals(right);\n\n");
        AppendDoc(output, "The value of a text as the API writes it; null is the default (empty) value, as a JSON null reads.", Indent);
        output.Append(Indent).Append($"public static implicit operator {name}(string? value) => value is null ? default : new(value);\n\n");
        AppendDoc(output, "Whether both values are the same (ordinal comparison).", Indent);
        output.Append(Indent).Append($"public bool Equals({name} other) => string.Equals(Value, other.Value, StringComparison.Ordinal);\n\n");
        AppendDoc(output, "Whether the object is the same value.", Indent);
        output.Append(Indent).Append($"public override bool Equals(object? obj) => obj is {name} other && Equals(other);\n\n");
        AppendDoc(output, "Orders the values by their text (ordinal comparison).", Indent);
        output.Append(Indent).Append($"public int CompareTo({name} other) => string.CompareOrdinal(Value, other.Value);\n\n");
        AppendDoc(output, "A hash code of the value.", Indent);
        output.Append(Indent).Append("public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);\n\n");
        AppendDoc(output, "The value as the API writes it.", Indent);
        output.Append(Indent).Append("public override string ToString() => Value;\n\n");
        output.Append(Indent).Append($"internal sealed class Converter : ExtensibleEnumConverter<{name}>\n");
        output.Append(Indent).Append("{\n");
        output.Append(Indent).Append(Indent).Append($"protected override {name} Create(string value) => new(value);\n");
        output.Append(Indent).Append("}\n");
        output.Append("}\n");
    }

    private static void EmitUnion(StringBuilder output, UnionModel union)
    {
        var name = union.Name;
        AppendDoc(output, $"{union.Doc} Match it on its type (`DocumentEvent`, `CertificateEvent`, ...); `{union.UnknownName}` stands for a type this version of the SDK does not know yet.", "");
        output.Append(GeneratedCode).Append('\n');
        output.Append($"[JsonConverter(typeof({name}JsonConverter))]\n");
        output.Append($"public abstract partial record {name} : {ExternalBase}\n{{\n");
        output.Append(Indent).Append($"private protected {name}()\n");
        output.Append(Indent).Append("{\n");
        output.Append(Indent).Append("}\n");
        output.Append("}\n\n");

        output.Append($"/// <summary>Reads an <see cref=\"{name}\"/> as the type its <c>{union.Discriminator}</c> names.</summary>\n");
        output.Append(GeneratedCode).Append('\n');
        output.Append($"internal sealed class {name}JsonConverter : JsonConverter<{name}>\n{{\n");
        output.Append(Indent).Append($"public override {name}? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)\n");
        output.Append(Indent).Append("{\n");
        output.Append(Indent).Append(Indent).Append("using var document = JsonDocument.ParseValue(ref reader);\n");
        output.Append(Indent).Append(Indent).Append("return ReadElement(document.RootElement, options);\n");
        output.Append(Indent).Append("}\n\n");
        output.Append(Indent).Append($"internal static {name} ReadElement(JsonElement element, JsonSerializerOptions options)\n");
        output.Append(Indent).Append("{\n");
        output.Append(Indent).Append(Indent).Append("if (element.ValueKind != JsonValueKind.Object)\n");
        output.Append(Indent).Append(Indent).Append("{\n");
        output.Append(Indent).Append(Indent).Append(Indent).Append($"throw new JsonException(\"An {name} must be a JSON object\");\n");
        output.Append(Indent).Append(Indent).Append("}\n");
        output.Append(Indent).Append(Indent).Append($"var discriminator = element.TryGetProperty(\"{union.Discriminator}\", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;\n");
        output.Append(Indent).Append(Indent).Append("return discriminator switch\n");
        output.Append(Indent).Append(Indent).Append("{\n");
        foreach (var group in union.Mapping.GroupBy(entry => entry.Member))
        {
            var values = string.Join(" or ", group.Select(entry => Literal(entry.Value)));
            output.Append(Indent).Append(Indent).Append(Indent)
                .Append($"{values} => Read<{group.Key}>(element, options),\n");
        }
        output.Append(Indent).Append(Indent).Append(Indent).Append($"_ => Read<{union.UnknownName}>(element, options),\n");
        output.Append(Indent).Append(Indent).Append("};\n");
        output.Append(Indent).Append("}\n\n");
        output.Append(Indent).Append($"public override void Write(Utf8JsonWriter writer, {name} value, JsonSerializerOptions options)\n");
        output.Append(Indent).Append("{\n");
        output.Append(Indent).Append(Indent).Append("switch (value)\n");
        output.Append(Indent).Append(Indent).Append("{\n");
        foreach (var member in union.Mapping.Select(entry => entry.Member).Distinct().Append(union.UnknownName))
        {
            output.Append(Indent).Append(Indent).Append(Indent).Append($"case {member} item:\n");
            output.Append(Indent).Append(Indent).Append(Indent).Append(Indent)
                .Append($"JsonSerializer.Serialize(writer, item, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<{member}>)options.GetTypeInfo(typeof({member})));\n");
            output.Append(Indent).Append(Indent).Append(Indent).Append(Indent).Append("break;\n");
        }
        output.Append(Indent).Append(Indent).Append(Indent).Append("default:\n");
        output.Append(Indent).Append(Indent).Append(Indent).Append(Indent)
            .Append($"throw new JsonException($\"Cannot write the {name} {{value.GetType()}}\");\n");
        output.Append(Indent).Append(Indent).Append("}\n");
        output.Append(Indent).Append("}\n\n");
        output.Append(Indent).Append("private static T Read<T>(JsonElement element, JsonSerializerOptions options)\n");
        output.Append(Indent).Append(Indent).Append("where T : class =>\n");
        output.Append(Indent).Append(Indent)
            .Append("element.Deserialize((System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)options.GetTypeInfo(typeof(T))) ?? throw new JsonException(\"An event cannot be null\");\n");
        output.Append("}\n");
    }

    private void EmitContext(StringBuilder output)
    {
        // System.Text.Json's source generator marks the context as generated code itself.
        output.Append("/// <summary>The System.Text.Json metadata of every model, generated at build time.</summary>\n");
        output.Append("[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]\n");
        output.Append($"[JsonSerializable(typeof({ExternalBase}))]\n");
        foreach (var model in _types.Values.Where(model => model is RecordModel or UnionModel).OrderBy(model => model.Name, StringComparer.Ordinal))
        {
            output.Append($"[JsonSerializable(typeof({model.Name}))]\n");
        }
        output.Append("internal sealed partial class FlashpiJsonContext : JsonSerializerContext;\n\n");

        output.Append("/// <summary>The members of request records that are sent only when set, by type.</summary>\n");
        output.Append(GeneratedCode).Append('\n');
        output.Append("internal static class PresenceTracking\n{\n");
        output.Append(Indent).Append("internal static readonly IReadOnlyDictionary<Type, string[]> Members = new Dictionary<Type, string[]>\n");
        output.Append(Indent).Append("{\n");
        foreach (var record in _types.Values.OfType<RecordModel>().Where(record => record.Properties.Any(property => property.TriState)).OrderBy(record => record.Name, StringComparer.Ordinal))
        {
            var names = string.Join(", ", record.Properties.Where(property => property.TriState).Select(property => Literal(property.JsonName)));
            output.Append(Indent).Append(Indent).Append($"[typeof({record.Name})] = [{names}],\n");
        }
        output.Append(Indent).Append("};\n");
        output.Append("}\n");
    }

    private static string Literal(string value) => JsonSerializer.Serialize(value, LiteralOptions);

    /// <summary>A <c>/// &lt;summary&gt;</c> comment, wrapped to the line length, with Markdown code
    /// spans as <c>&lt;c&gt;</c>.</summary>
    private static void AppendDoc(StringBuilder output, string text, string indent)
    {
        // Control characters and Unicode line breaks would end the comment's line.
        var clean = new string([.. text.Select(character => char.IsControl(character) || character is '\u2028' or '\u2029' ? ' ' : character)]);
        var escaped = clean.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
        escaped = CodeSpan().Replace(escaped, "<c>$1</c>");
        var prefix = $"{indent}/// ";
        var single = $"{prefix}<summary>{escaped}</summary>";
        if (single.Length <= LineLength)
        {
            output.Append(single).Append('\n');
            return;
        }
        output.Append(prefix).Append("<summary>\n");
        var width = LineLength - prefix.Length;
        var line = new StringBuilder();
        foreach (var word in escaped.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                output.Append(prefix).Append(line).Append('\n');
                line.Clear();
            }
            if (line.Length > 0)
            {
                line.Append(' ');
            }
            line.Append(word);
        }
        if (line.Length > 0)
        {
            output.Append(prefix).Append(line).Append('\n');
        }
        output.Append(prefix).Append("</summary>\n");
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeSpan();
}

/// <summary>A C# type of a member.</summary>
internal sealed record TypeRef(string Name, bool IsValueType, bool Nullable)
{
    public string Expression => Nullable ? Name + "?" : Name;
}

internal sealed record PropertyModel(string JsonName, string Name, TypeRef Type, bool Required, bool TriState, string Doc)
{
    /// <summary>A secret, which the record's <c>ToString()</c> hides.</summary>
    public bool Secret { get; init; }
}

internal abstract class TypeModel(string name, string doc)
{
    public string Name { get; } = name;

    public string Doc { get; } = doc;
}

internal sealed class RecordModel(string name, string doc) : TypeModel(name, doc)
{
    public List<PropertyModel> Properties { get; } = [];

    /// <summary>The members its base declares (<c>WebhookEvent</c>'s).</summary>
    public HashSet<string> Inherited { get; init; } = new(StringComparer.Ordinal);

    public string? Base { get; init; }

    public bool IsRequest { get; init; }

    public bool IsQuery { get; init; }

    public bool HasExtensionData { get; set; }

    public bool HasSecret { get; set; }

    /// <summary>The item type when it is a page of a list (<c>data</c> and <c>pagination</c>).</summary>
    public string? ListItem { get; set; }
}

internal sealed class EnumModel(string name, string doc) : TypeModel(name, doc)
{
    public List<string> Values { get; } = [];
}

internal sealed class UnionModel(string name, string doc, string discriminator, string unknownName) : TypeModel(name, doc)
{
    public string Discriminator { get; } = discriminator;

    public string UnknownName { get; } = unknownName;

    public List<(string Value, string Member)> Mapping { get; } = [];
}
