using System.Text.Json;
using FluentAssertions;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Tests.Models;

public class OASISResultTests
{
    [Fact]
    public void OASISResult_Default_ShouldHaveIsErrorFalse()
    {
        var result = new OASISResult<string>();
        result.IsError.Should().BeFalse();
        result.Message.Should().BeEmpty();
        result.Result.Should().BeNull();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public void OASISResult_WithValues_ShouldPreserveThem()
    {
        var result = new OASISResult<int>
        {
            IsError = false,
            Message = "Success",
            Result = 42
        };

        result.Result.Should().Be(42);
        result.Message.Should().Be("Success");
    }

    [Fact]
    public void OASISResult_Error_ShouldIncludeException()
    {
        var ex = new InvalidOperationException("boom");
        var result = new OASISResult<string>
        {
            IsError = true,
            Message = "boom",
            Exception = ex
        };

        result.Exception.Should().Be(ex);
    }

    [Fact]
    public void OASISResponse_Default_ShouldHaveIsErrorFalse()
    {
        var response = new OASISResponse();
        response.IsError.Should().BeFalse();
        response.Message.Should().BeEmpty();
        response.Exception.Should().BeNull();
    }

    [Fact]
    public void Serialization_OASISResult_ShouldRoundTrip()
    {
        var original = new OASISResult<string> { Result = "test", Message = "ok" };
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OASISResult<string>>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Result.Should().Be("test");
        deserialized.Message.Should().Be("ok");
        deserialized.IsError.Should().BeFalse();
    }

    [Fact]
    public void Serialization_OASISResult_RawException_IsNeverSerialized()
    {
        // The raw Exception is [JsonIgnore] — serializing it is unsafe and
        // leaks internals. Verbose detail is exposed via Detail, gated by
        // debug mode (see the debug-mode tests below).
        var original = new OASISResult<string>
        {
            IsError = true,
            Message = "error",
            Exception = new Exception("hidden")
        };

        var json = JsonSerializer.Serialize(original);

        json.Should().NotContain("hidden");
        json.Should().NotContain("StackTrace");
    }

    [Fact]
    public void Detail_IsNull_WhenDebugDisabled()
    {
        var prev = OASISResultDebug.Enabled;
        try
        {
            OASISResultDebug.Enabled = false;
            var result = new OASISResult<string>()
                .CaptureException(new InvalidOperationException("boom"));

            result.IsError.Should().BeTrue();
            result.Message.Should().Be("boom");
            result.Detail.Should().BeNull();
        }
        finally { OASISResultDebug.Enabled = prev; }
    }

    [Fact]
    public void Detail_IncludesExceptionChain_WhenDebugEnabled()
    {
        var prev = OASISResultDebug.Enabled;
        try
        {
            OASISResultDebug.Enabled = true;
            var ex = new InvalidOperationException("outer", new IOException("inner cause"));
            var result = new OASISResult<string>().CaptureException(ex);

            result.Detail.Should().NotBeNull();
            result.Detail!.Type.Should().Contain("InvalidOperationException");
            result.Detail.Message.Should().Be("outer");
            result.Detail.Inner.Should().NotBeNull();
            result.Detail.Inner!.Message.Should().Be("inner cause");

            var payload = JsonSerializer.Serialize(result.ToErrorPayload(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            payload.Should().Contain("\"error\":\"outer\"");
            payload.Should().Contain("\"detail\"");
            payload.Should().Contain("inner cause");
        }
        finally { OASISResultDebug.Enabled = prev; }
    }

    [Fact]
    public void ToErrorPayload_OmitsDetail_WhenDebugDisabled()
    {
        var prev = OASISResultDebug.Enabled;
        try
        {
            OASISResultDebug.Enabled = false;
            // Internal exception ("secret internals") + public-facing summary.
            var result = new OASISResult<string>()
                .CaptureException(
                    new InvalidOperationException("secret internals"),
                    "Something went wrong.");

            var payload = JsonSerializer.Serialize(result.ToErrorPayload(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            // The chosen summary message is surfaced...
            payload.Should().Contain("Something went wrong.");
            // ...but no verbose internals (type, stack trace, inner chain) leak.
            payload.Should().Contain("\"detail\":null");
            payload.Should().NotContain("secret internals");
            payload.Should().NotContain("InvalidOperationException");
            result.Detail.Should().BeNull();
        }
        finally { OASISResultDebug.Enabled = prev; }
    }

    [Fact]
    public void Serialization_OASISResult_WithNullResult_ShouldRoundTrip()
    {
        var original = new OASISResult<object> { IsError = true, Message = "Not found", Result = null };
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OASISResult<object>>(json);

        deserialized!.IsError.Should().BeTrue();
        deserialized.Result.Should().BeNull();
    }

    [Fact]
    public void Serialization_OASISResult_WithComplexType_ShouldRoundTrip()
    {
        var original = new OASISResult<Dictionary<string, int>>
        {
            Result = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }
        };
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OASISResult<Dictionary<string, int>>>(json);

        deserialized!.Result.Should().ContainKey("a").WhoseValue.Should().Be(1);
    }

    [Fact]
    public void Serialization_OASISResponse_ShouldRoundTrip()
    {
        var original = new OASISResponse { Message = "Deleted." };
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OASISResponse>(json);

        deserialized!.Message.Should().Be("Deleted.");
        deserialized.IsError.Should().BeFalse();
    }
}
