using System.ClientModel;
using HarrisCountyAI.Infrastructure.Resilience;
using HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

namespace HarrisCountyAI.UnitTests.Resilience;

public class RetryAfterHeaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("1", 1)]
    [InlineData("30", 30)]
    [InlineData("60", 60)]
    [InlineData("  47  ", 47)] // whitespace-padded, as some proxies send it
    public void Delta_Seconds_Are_Read_As_A_Delay(string header, int expectedSeconds)
    {
        var exception = FakeClientResultExceptions.WithRetryAfter(429, header);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryAfterHeader.Read(exception, Now));
    }

    [Fact]
    public void Fractional_Seconds_Are_Accepted()
    {
        // Not RFC 9110, but some services send it and rounding down would retry early.
        var exception = FakeClientResultExceptions.WithRetryAfter(429, "2.5");

        Assert.Equal(TimeSpan.FromSeconds(2.5), RetryAfterHeader.Read(exception, Now));
    }

    [Fact]
    public void Zero_Seconds_Is_A_Hint_Of_No_Delay_Not_A_Missing_Hint()
    {
        var exception = FakeClientResultExceptions.WithRetryAfter(429, "0");

        Assert.Equal(TimeSpan.Zero, RetryAfterHeader.Read(exception, Now));
    }

    [Fact]
    public void Http_Date_Is_Measured_From_Now()
    {
        var exception = FakeClientResultExceptions.WithRetryAfter(503, "Thu, 13 Aug 2026 12:00:45 GMT");

        Assert.Equal(TimeSpan.FromSeconds(45), RetryAfterHeader.Read(exception, Now));
    }

    [Fact]
    public void Http_Date_Already_Past_Clamps_To_Zero()
    {
        // Never returns a negative delay, so callers can treat any value as a floor.
        var exception = FakeClientResultExceptions.WithRetryAfter(503, "Thu, 13 Aug 2026 11:59:00 GMT");

        Assert.Equal(TimeSpan.Zero, RetryAfterHeader.Read(exception, Now));
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-5")]
    public void Unreadable_Values_Are_Reported_As_No_Hint(string header)
    {
        var exception = FakeClientResultExceptions.WithRetryAfter(429, header);

        Assert.Null(RetryAfterHeader.Read(exception, Now));
    }

    [Fact]
    public void Failure_Without_The_Header_Has_No_Hint()
    {
        Assert.Null(RetryAfterHeader.Read(FakeClientResultExceptions.WithStatus(429), Now));
    }

    [Fact]
    public void Null_Exception_Has_No_Hint()
    {
        Assert.Null(RetryAfterHeader.Read(null, Now));
    }

    [Fact]
    public void Exception_Without_A_Response_Has_No_Hint()
    {
        Assert.Null(RetryAfterHeader.Read(new InvalidOperationException("no response"), Now));
    }

    [Fact]
    public void Azure_Core_Failures_Are_Read_Too()
    {
        // Search, Blob, and Document Intelligence throw RequestFailedException
        // rather than the ClientResultException the OpenAI clients throw.
        var exception = FakeRequestFailedExceptions.WithRetryAfter(429, "12");

        Assert.Equal(TimeSpan.FromSeconds(12), RetryAfterHeader.Read(exception, Now));
        Assert.Null(RetryAfterHeader.Read(FakeRequestFailedExceptions.WithoutHeaders(429), Now));
    }

    [Fact]
    public void Wrapped_Failures_Are_Unwrapped()
    {
        var inner = FakeClientResultExceptions.WithRetryAfter(429, "20");
        var wrapped = new InvalidOperationException("pipeline failed", inner);

        Assert.Equal(TimeSpan.FromSeconds(20), RetryAfterHeader.Read(wrapped, Now));
    }

    [Fact]
    public void Aggregate_Failures_Use_The_First_Hint_Found()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("no response here"),
            FakeClientResultExceptions.WithRetryAfter(429, "9"));

        Assert.Equal(TimeSpan.FromSeconds(9), RetryAfterHeader.Read(aggregate, Now));
    }

    [Fact]
    public void Public_Overload_Measures_Against_The_Current_Time()
    {
        // The delta-seconds form does not depend on the clock, so the overload
        // that supplies DateTimeOffset.UtcNow is exercised without a fixed now.
        var exception = FakeClientResultExceptions.WithRetryAfter(429, "15");

        Assert.Equal(TimeSpan.FromSeconds(15), RetryAfterHeader.Read(exception));
    }

    [Fact]
    public void A_Ninety_Second_Window_Is_Reported_In_Full()
    {
        // The value that caused the original failure: four retries inside two
        // seconds against a window this long can never succeed. Capping is the
        // caller's decision, so the reader reports what the service said.
        var exception = FakeClientResultExceptions.WithRetryAfter(429, "90");

        Assert.Equal(TimeSpan.FromSeconds(90), RetryAfterHeader.Read(exception, Now));
    }

    [Fact]
    public void A_Client_Failure_Carrying_The_Header_Is_Still_Read()
    {
        // Whether to retry is TransientFailureClassifier's decision, not this
        // reader's; it only reports what the response said.
        ClientResultException exception = FakeClientResultExceptions.WithRetryAfter(400, "5");

        Assert.Equal(TimeSpan.FromSeconds(5), RetryAfterHeader.Read(exception, Now));
    }
}
