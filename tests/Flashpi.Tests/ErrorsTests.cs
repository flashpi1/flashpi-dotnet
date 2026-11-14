using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Flashpi.Internal;
using Flashpi.Models;
using Xunit;

namespace Flashpi.Tests;

public sealed class ErrorsTests
{
    public static TheoryData<int, Type> Classes => new()
    {
        { 400, typeof(BadRequestException) },
        { 401, typeof(AuthenticationException) },
        { 403, typeof(PermissionDeniedException) },
        { 404, typeof(NotFoundException) },
        { 409, typeof(ConflictException) },
        { 422, typeof(UnprocessableEntityException) },
        { 429, typeof(RateLimitException) },
        { 418, typeof(ApiException) },
        { 501, typeof(InternalServerException) },
    };

    [Theory]
    [MemberData(nameof(Classes))]
    public async Task Maps_the_status_to_its_class(int status, Type type)
    {
        var api = new FakeApi(Reply.Problem(status, "some_code"));
        using var test = new Harness(api, options => { options.MaxRetries = 0; return options; });
        var error = await Assert.ThrowsAnyAsync<ApiException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Equal(type, error.GetType());
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task Exposes_the_problem_details_and_their_extensions()
    {
        var api = new FakeApi(Reply.Problem(422, "validation_failed", new
        {
            errors = new[] { new { field = "items[0].quantity", code = "invalid_format", message = "Bad" } },
            documentId = Samples.OtherId,
            documentStatus = "authorized",
            somethingNew = new { a = 1 },
        }));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice()));
        Assert.Equal(ErrorCode.ValidationFailed, error.Code);
        Assert.Equal("Problem", error.Title);
        Assert.Equal("Detail of validation_failed", error.Detail);
        Assert.Equal("422 validation_failed: Detail of validation_failed", error.Message);
        Assert.Equal("req_problem", error.RequestId);
        var field = Assert.Single(error.Errors);
        Assert.Equal("items[0].quantity", field.Field);
        Assert.Equal(FieldErrorCode.InvalidFormat, field.Code);
        Assert.Equal(Samples.OtherId, error.Problem!.DocumentId);
        Assert.Equal(DocumentStatus.Authorized, error.Problem.DocumentStatus);
        Assert.Equal(1, error.Problem.AdditionalProperties!["somethingNew"].GetProperty("a").GetInt32());
        Assert.Equal("application/problem+json", error.GetHeader("content-type"));
    }

    [Fact]
    public async Task Keeps_an_unknown_code()
    {
        var api = new FakeApi(Reply.Problem(409, "brand_new_code"));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<ConflictException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Equal("brand_new_code", error.Code?.Value);
        Assert.Equal(new ErrorCode("brand_new_code"), error.Code);
    }

    [Fact]
    public async Task Handles_an_error_body_that_is_not_problem_details()
    {
        var api = new FakeApi(Reply.Text("<html>\n  <body>Bad   gateway</body>\n</html>", 502), Reply.Json("{\"message\":\"x\"}", 400), Reply.Empty(500));
        using var test = new Harness(api, options => { options.MaxRetries = 0; return options; });
        var gateway = await Assert.ThrowsAsync<InternalServerException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Null(gateway.Code);
        Assert.Null(gateway.Problem);
        Assert.Equal("502 <html> <body>Bad gateway</body> </html>", gateway.Message);
        Assert.Null(gateway.RequestId);
        var bad = await Assert.ThrowsAsync<BadRequestException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Null(bad.Code);
        Assert.Equal(Reply.RequestId, bad.RequestId);
        var empty = await Assert.ThrowsAsync<InternalServerException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Equal("500 (no response body)", empty.Message);
    }

    [Fact]
    public async Task Keeps_what_it_can_read_of_problem_details_of_another_shape()
    {
        var api = new FakeApi(Reply.Json("{\"code\":\"validation_failed\",\"status\":\"422\",\"detail\":\"Bad\",\"requestId\":\"r1\"}", 422));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Equal(ErrorCode.ValidationFailed, error.Code);
        Assert.Equal("Bad", error.Detail);
        Assert.Equal("r1", error.RequestId);
        Assert.Empty(error.Errors);
    }

    [Fact]
    public async Task Treats_a_redirect_as_an_error_without_following_it()
    {
        var redirect = Reply.Empty(302);
        redirect.Headers.Location = new Uri("https://elsewhere.example/");
        var api = new FakeApi(redirect);
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<ApiException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Equal(302, error.StatusCode);
        Assert.Single(api.Requests);
    }

    [Fact]
    public void Reads_retry_after_in_seconds_or_as_a_date()
    {
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(30), RetryAfterHeader.Parse(" 30 ", now));
        Assert.Equal(TimeSpan.Zero, RetryAfterHeader.Parse("0", now));
        Assert.Equal(TimeSpan.MaxValue, RetryAfterHeader.Parse("9999999999999999999999", now));
        Assert.Equal(TimeSpan.MaxValue, RetryAfterHeader.Parse("999999999999999999", now));
        Assert.Equal(TimeSpan.FromSeconds(90), RetryAfterHeader.Parse("Fri, 13 Nov 2026 12:01:30 GMT", now));
        Assert.Equal(TimeSpan.Zero, RetryAfterHeader.Parse("Fri, 13 Nov 2026 11:00:00 GMT", now));
        Assert.Equal(TimeSpan.FromSeconds(2), RetryAfterHeader.Parse("2026-11-13T12:00:01.5Z", now));
        Assert.Null(RetryAfterHeader.Parse("soon", now));
        Assert.Null(RetryAfterHeader.Parse("-5", now));
        Assert.Null(RetryAfterHeader.Parse(null, now));
    }

    [Fact]
    public void Webhook_verification_errors_carry_their_reason()
    {
        var error = new WebhookVerificationException(WebhookVerificationReason.InvalidSecret, "bad");
        Assert.Equal(WebhookVerificationReason.InvalidSecret, error.Reason);
        Assert.IsAssignableFrom<FlashpiException>(error);
        Assert.IsAssignableFrom<ApiConnectionException>(new ApiTimeoutException("slow"));
        Assert.Null(new FlashpiException("x").IdempotencyKey);
    }

    [Fact]
    public void Describes_long_bodies_by_their_first_200_characters()
    {
        var message = ApiErrors.Message(502, null, new string('a', 300));
        Assert.Equal("502 " + new string('a', 200), message);
        Assert.Equal("409 sequential_already_used", ApiErrors.Message(409, new ProblemDetails
        {
            Type = "t",
            Title = null!,
            Status = 409,
            Detail = null!,
            Instance = "/",
            Code = ErrorCode.SequentialAlreadyUsed,
            Errors = new List<FieldError>(),
            RequestId = "r",
        }, ""));
    }
}
