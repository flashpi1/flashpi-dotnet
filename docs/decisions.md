# Decisions

Decisions about Flashpi, the .NET SDK, newest first. Its design follows the Python SDK,
[flashpi](https://github.com/flashpi1/flashpi-python), and through it the Node.js SDK,
[@flashpi/node](https://github.com/flashpi1/flashpi-node), whose
[decisions](https://github.com/flashpi1/flashpi-python/blob/main/docs/decisions.md) apply here
unless an entry says otherwise. Entries marked as the owner's were taken with the Flashpi product
owner; the others were chosen while porting, after the Python SDK where the owner had not decided,
and are listed so they can be confirmed or changed before 1.0.

## 2026-11-13: the .NET SDK

### The owner's decisions

- **Package and repository.** `Flashpi` on NuGet (namespace `Flashpi`), public repository
  `flashpi1/flashpi-dotnet`, MIT license held by SIGNIADIGITAL S.A. First version 0.1.0 through
  release-please (the manifest starts at 0.0.0 with `initial-version` 0.1.0).
- **Targets.** `net10.0` and `netstandard2.0`. The netstandard2.0 build serves .NET Framework
  4.6.2 and later and .NET 8 and 9, and only it depends on packages: System.Text.Json and
  Microsoft.Bcl.AsyncInterfaces (10.0.15). net10.0 has no dependencies.
- **Models.** Records generated from the OpenAPI document, with typed PascalCase properties and
  System.Text.Json, with source generation (an internal `JsonSerializerContext`). The generator is
  a committed tool (`tools/Flashpi.Tools`) with a `check` mode that CI runs.
- **Async only.** `Task` methods with a `CancellationToken`
  (`await flashpi.Invoices.CreateAsync(...)`), and `await foreach` for lists (`IAsyncEnumerable`).
- **Tooling.** xUnit, nullable reference types, the .NET analyzers and code style with warnings as
  errors, `dotnet format`. .NET runs from the official Docker images on the maintainers' Macs.
- **Publishing.** NuGet trusted publishing (OIDC) from GitHub Actions, behind a GitHub environment
  `nuget` that the owner approves, for every version including 0.1.0.
- **Everything else as the Python SDK.** READMEs in Spanish (`README.md`) and English
  (`README.en.md`); resource style; an Idempotency-Key on every POST, reused by its retries; up to
  2 retries with backoff and jitter after network errors, timeouts, 408, 429 and 5xx (not 501 or
  `demo_issuer_unavailable`), honoring a Retry-After of 60 s or less; waiting for a 409
  `idempotency_key_in_progress` when an earlier attempt may have reached the API; the document of
  a retried POST answered `idempotency_key_reused`; a retried DELETE answered 404 counts as done;
  one deadline per attempt; API key and header validation; English errors with `Language.Spanish`
  per client or per call; the `FLASHPI_API_KEY` fallback; Standard Webhooks verification;
  release-please from 0.1.0; the live API test on the release PR only; the flashpi-bot GitHub App
  token for bot PRs; grouped Dependabot.

### Chosen while porting (to confirm)

- **Names.** The client is `FlashpiClient` with `FlashpiClientOptions` (a class named like its
  namespace makes `Flashpi.Flashpi`, which C# code avoids). The client, its options,
  `RequestOptions`, `Language`, `ApiResponse`, `Page`, `Paginator`, `Webhooks` and the exceptions
  are in `Flashpi`; the models in `Flashpi.Models` (as Python's `flashpi.types`); the resources in
  `Flashpi.Resources`. The environment is `FlashpiEnvironment`, since `Environment` would clash
  with `System.Environment` in every file that reads a variable; the API's `Status` schema is
  `ApiStatus` (with `ApiStatusKind`), since a C# type cannot have a member named like itself.
- **Exceptions.** .NET names: `FlashpiException`, `ApiException` and its subclasses by status
  (`BadRequestException` to `InternalServerException`), `ApiConnectionException` and
  `ApiTimeoutException` (Python's names, as `TimeoutException` would clash with
  `System.TimeoutException`), `WebhookVerificationException` with a `WebhookVerificationReason`
  enum. `ApiException` has `StatusCode` (an `int`), `Code` (an `ErrorCode?`), `Title`, `Detail`,
  `Errors`, `RequestId`, `Problem`, the `HttpResponseMessage` as `Response`, `GetHeader(name)` and
  `RetryAfter` as a `TimeSpan?`. Their constructors are internal, except those of the connection
  and webhook errors.
- **Misused arguments.** `ArgumentNullException` for null bodies, uploads, payloads and headers;
  `ArgumentException` for header names and values (control characters, `Content-Length`,
  `Transfer-Encoding`), a .p12 file name that is not printable ASCII, and lone surrogates in ids
  or query values; `ArgumentOutOfRangeException` for a negative webhook tolerance. Timeouts,
  `MaxRetries`, ids (null ones included), `BaseUrl` and the API key stay `FlashpiException`, as in
  Node and Python.
- **Headers.** Python's rules, with one difference: a header value with a character outside ASCII
  throws `ArgumentException` instead of being sent as UTF-8, since .NET's handlers refuse it
  (SocketsHttpHandler without an encoding selector, .NET Framework) and a refusal while sending
  would be retried as a network error. Content headers (`Content-Type`, `Content-Language`, ...)
  in `DefaultHeaders` or `ExtraHeaders` are sent only with a body; a JSON body is always
  `application/json` and a multipart one keeps its boundary.
- **Models.** `sealed partial record`s with `init` properties; members the schema requires are
  `required`, so the compiler asks for them (in .NET Framework projects and netstandard2.0
  libraries, C# 11 or later; Visual Basic cannot set them). Answers are read leniently, as the
  other SDKs read them: a member the contract requires but the answer lacks, or sends as `null`,
  reads as its default instead of failing, and unknown members are ignored, except in
  `ProblemDetails` and `UnknownEvent`, which keep them in a settable `AdditionalProperties`
  (System.Text.Json cannot fill extension data through a constructor). The `ToString()` of
  `WebhookEndpointSecret` hides its secret (a table of secret members in the generator). `date-time` is `DateTimeOffset`; `date`,
  `uuid`, amounts and quantities stay strings (`DateOnly` is not in netstandard2.0); `integer` is
  `int` (`long` for `int64`) and `number` is `double`.
- **Enums.** Extensible `readonly struct`s: a value the SDK does not know yet still reads (and
  compares by its `Value`), with a static member per known value, `==`, ordinal ordering
  (`IComparable`), use as dictionary keys in JSON, and an implicit conversion from text
  (`IdType = "ruc"`). A null text converts to the default (empty) value, as a JSON null reads, so
  `status == null` and `status.Equals(null)` are false instead of throwing, and a null `Language`
  sends no header. In C#, a `null` in a conditional such as `cond ? new RimpeRegime(x) : null`
  converts through that conversion into the empty value, which the API refuses:
  `default(RimpeRegime?)` sends `null`. Inline enums are named after their schema and member
  (`CertificateStatus`), except where one type serves several places (`FlashpiEnvironment`,
  `EventType`, `DocumentStatus`, `DocumentType`, `RimpeRegime`, `IdType`, `PaymentMethod`,
  `TaxType`, `RideStatus`, `PayloadMode`, `WebhookDeliveryStatus`), a table in the generator.
  `EventType` also holds `*` (`EventType.All`, the subscription to every type) and
  `webhook_endpoint.verification`. Values that start with a digit get a `Value` prefix
  (`SupportingDocumentTaxRate.Value15`).
- **Requests.** An optional nullable member of a request body is sent only when set: setting it to
  `null` sends `null` (an update that clears `TradeName`), leaving it out sends nothing. Every
  list's query is a record named in a table of the generator (`DocumentListParams`, ...), sent with
  the API's names; a new list operation fails the generator until it gets a name.
- **Events.** `Event` is an abstract record read by its `type` (`DocumentEvent`,
  `CertificateEvent`, `AccountEvent`, or `UnknownEvent` for a type the SDK does not know yet), and
  `WebhookEvent`, written by hand, is the base of everything a webhook endpoint receives (`Event`,
  `EventThin` and `WebhookEndpointVerificationEvent`), with `Id`, `Type`, `Environment` and
  `CreatedAt`. `Webhooks.Unwrap` returns the right one by itself, a thin event being one without
  `data`, so there is no counterpart of Python's cast.
- **Shape of the API.** Methods end in `Async` and take the path ids, then the body or the list's
  parameters, then `RequestOptions? options = null` and `CancellationToken cancellationToken =
  default`. Python's `xml` and `pdf` are `GetXmlAsync` (a `DocumentXml` record with a
  `DocumentXmlKind?`) and `GetPdfAsync` (a `byte[]`); a DELETE returns a `Task`. The six document
  types share `DocumentIssuer<TBody>`. `IssuerOnboardings.CompleteAsync(id)` sends `{}`,
  `WebhookEndpoints.RotateSecretAsync(id)` no body, and `Documents.SendEmailAsync(id)` always `{}`.
  Python's `with_response` view is `WithResponse` on every resource, with the same methods
  returning an `ApiResponse<T>` (`Data`, `Response`, `StatusCode`, `RequestId`, `GetHeader`), or
  an `ApiResponse` for a DELETE.
- **Lists.** `ListAsync` returns a `Paginator<T>`: `await foreach` walks every item across pages
  and `await` gives the first `Page<T>` (it has a `GetAwaiter`, as Python's `AsyncPaginator`
  is awaitable); a `Page<T>` has `Data`, `HasMore`, `NextCursor`, `HasNextPage` and
  `GetNextPageAsync()`, and is walked with `await foreach` too. The first page is fetched once (a
  failure is tried again on the next await). The token given to `ListAsync` applies to every
  page's request, together with the enumerator's. `ListAsync` checks the path ids when called, as
  Python's `list`; the `Task` methods report every failure through the task.
- **Requests and timeouts.** Timeouts are `TimeSpan`s up to 2147483.647 s (Node's limit, and
  `CancelAfter`'s). The SDK's own `HttpClient` never follows redirects, decompresses answers, has
  an infinite `Timeout` and, on net10.0, renews pooled connections every 5 minutes (DNS changes).
  On .NET Framework 4.7.1 and later it also lifts the limit of 2 connections per server, as .NET
  has none, so concurrent calls do not wait for a connection while their deadline runs. A caller's
  `HttpClient` is used as it is: the caller gives it an infinite timeout and a handler that does
  not follow redirects. A body that does not decompress is a retried connection error, and a
  client used after it was disposed throws `ObjectDisposedException` at once. Each attempt has one deadline, from sending it to reading the
  whole answer: a token that cancels at the timeout, which also disposes the answer, since a
  .NET Framework stream may not observe it. Cancelling through the caller's token throws
  `OperationCanceledException` with that token, and is never retried. The answer an
  `ApiResponse` or `ApiException` holds keeps its request without the `Authorization` header.
  `BaseUrl` is an http(s) `Uri` with a host and no query or fragment.
- **Certificates.** `CertificateUpload` takes the .p12 as bytes or a readable `Stream` (read once,
  asynchronously, up to one byte past 1 MiB, and not disposed), the password, and an optional
  `FileName`; its `ToString()` shows neither the file nor the password. The multipart body sends
  the `password` field, then the `file` part as `application/x-pkcs12`.
- **Webhooks.** A static `Webhooks` class with `Unwrap` and `VerifySignature` (no client
  property: the helpers need no key), taking the body as text or `byte[]` and the headers as
  ASP.NET Core's `IHeaderDictionary`, `HttpHeaders`, any dictionary or a `NameValueCollection`
  (ASP.NET on .NET Framework, whose repeated values are joined with commas again). `tolerance` is
  a `TimeSpan` (5 minutes by default) and `now` a `DateTimeOffset`. A signed body that is not a
  JSON object, or whose members are not of the contract's types, is `InvalidPayload`. The decoded
  key and the expected signature are cleared once used.
- **Secrets.** The API key never appears in `ToString()` of the client, its options or its
  configuration, nor in any message. .NET exceptions do not keep the local variables of their
  frames, so Python's frame scrubbing has no counterpart.
- **Tooling.** xUnit v3 on Microsoft.Testing.Platform (`dotnet test --project ...`, as the .NET
  10 SDK requires for it), coverlet.MTP for coverage with a 95 % gate on the hand-written code
  (`coverage check` of the tool; the generated models carry `[GeneratedCode]` and are left out),
  the language (C# 14) and the analyzers (`10.0-recommended`) pinned so a newer SDK builds the
  same, central package management, and package validation of both builds' API. The tests run on
  net10.0, on net11.0 (the net10.0 build, with the .NET 11 SDK), on net8.0 and on .NET Framework
  4.8 on Windows (both with the netstandard2.0 build). A Python-like generator written in C#
  instead of NSwag or Kiota: no large dependency, a deterministic output, and a failure on any
  construct it does not know (also on schema names that are not identifiers, parameters declared
  for a whole path, an event type the discriminator does not map, a member shared by name with
  other values, and table entries that match nothing). Text of the document never ends a comment
  line. `global.json` rolls forward within 10.0; the .NET 11 job rolls forward on purpose. An
  OpenAPI sync PR whose checks fail is opened as a draft.
- **README examples.** Every `csharp` block of both READMEs becomes a file of
  `tests/Flashpi.Examples` (`readme generate`), built as a web project with the implicit usings of
  a user's project and the usings of each README's first block, so the ASP.NET Core examples
  compile against the real framework. The READMEs link to each other and to the license with
  absolute URLs, since NuGet shows `README.md` and cannot follow relative links.
- **Package.** The `.nupkg` holds the two builds with their XML documentation and `README.md`;
  the `.snupkg` their symbols; SourceLink points at the commit. `package check` fails on anything
  missing or extra and on other dependencies, and CI installs the package in a clean project
  (`tests/Flashpi.InstalledCheck`) on net10.0, net8.0 and net48. No icon yet.
- **Releases.** release-please's `simple` type updates `version.txt`; `Flashpi.csproj` and
  `src/Flashpi/Internal/SdkVersion.cs` (the User-Agent `flashpi-dotnet/<version>`) are extra files.
  A build job, with a read-only token, no secret and no cache, packs the tag with the .NET SDK
  10.0.404 (pinned by version, written into `global.json` with roll-forward disabled, since the
  runner images also have newer SDKs) and checks the package against the tag. CI's "Pack and
  install" fails on a version other than 0.0.0 whose READMEs still say it is not on NuGet. The publish job, the only
  one with `id-token: write`, checks out nothing and runs no code of the repository or of its
  dependencies: it gets a one-hour key with NuGet/login for the nuget.org account `flashpi` and
  pushes the `.nupkg` and `.snupkg` with the runner's dotnet CLI. A nuget.org trusted publishing
  policy belongs to the account, not to a package, so 0.1.0 is published by the workflow too.
  Dependabot updates the actions and the test packages, never the netstandard2.0 build's
  dependencies, which are the lowest versions the package asks of its users.
