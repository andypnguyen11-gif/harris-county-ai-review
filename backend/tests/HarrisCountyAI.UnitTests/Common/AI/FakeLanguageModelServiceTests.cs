using HarrisCountyAI.Application.Common.AI;

namespace HarrisCountyAI.UnitTests.Common.AI;

public class FakeLanguageModelServiceTests
{
    private static ModelRequest CreateRequest(string userPrompt = "Evaluate this explanation.") => new()
    {
        SystemPrompt = "You are a compliance reviewer.",
        UserPrompt = userPrompt,
    };

    [Fact]
    public async Task GenerateAsync_Records_Requests_In_Order()
    {
        var fake = new FakeLanguageModelService();

        await fake.GenerateAsync(CreateRequest("first"), CancellationToken.None);
        await fake.GenerateAsync(CreateRequest("second"), CancellationToken.None);

        Assert.Equal(2, fake.CallCount);
        Assert.Equal("first", fake.Requests[0].UserPrompt);
        Assert.Equal("second", fake.Requests[1].UserPrompt);
        Assert.Equal("second", fake.LastRequest!.UserPrompt);
    }

    [Fact]
    public async Task GenerateAsync_Returns_Scripted_Responses_Then_Default()
    {
        var fake = new FakeLanguageModelService { DefaultContent = "default" };
        fake.EnqueueContent("one").EnqueueContent("two");

        var first = await fake.GenerateAsync(CreateRequest(), CancellationToken.None);
        var second = await fake.GenerateAsync(CreateRequest(), CancellationToken.None);
        var third = await fake.GenerateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("one", first.Content);
        Assert.Equal("two", second.Content);
        Assert.Equal("default", third.Content);
    }

    [Fact]
    public async Task GenerateAsync_Response_Factory_Sees_Triggering_Request()
    {
        var fake = new FakeLanguageModelService();
        fake.EnqueueResponse(request => fake.CreateResponse($"echo: {request.UserPrompt}"));

        var response = await fake.GenerateAsync(CreateRequest("hello"), CancellationToken.None);

        Assert.Equal("echo: hello", response.Content);
    }

    [Fact]
    public async Task GenerateAsync_Throws_Scripted_Exception()
    {
        var fake = new FakeLanguageModelService();
        fake.EnqueueException(new InvalidOperationException("model unavailable"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.GenerateAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal("model unavailable", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_Honors_Cancellation_During_Delay()
    {
        var fake = new FakeLanguageModelService { Delay = TimeSpan.FromSeconds(30) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fake.GenerateAsync(CreateRequest(), cts.Token));
    }

    [Fact]
    public async Task GenerateAsync_Throws_Immediately_When_Already_Canceled()
    {
        var fake = new FakeLanguageModelService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fake.GenerateAsync(CreateRequest(), cts.Token));

        Assert.Equal(0, fake.CallCount);
    }
}
