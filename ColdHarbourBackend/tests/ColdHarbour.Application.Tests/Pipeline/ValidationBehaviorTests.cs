using ColdHarbour.Application.Pipeline;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace ColdHarbour.Application.Tests.Pipeline;

// Minimal request/response stubs
internal record TestRequest : IRequest<TestResponse>;
internal record TestResponse(string Value);

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task PassesThrough_WhenNoValidators()
    {
        var behavior = new ValidationBehavior<TestRequest, TestResponse>([]);
        var handlerCalled = false;

        Task<TestResponse> Next()
        {
            handlerCalled = true;
            return Task.FromResult(new TestResponse("ok"));
        }

        var result = await behavior.Handle(new TestRequest(), Next, CancellationToken.None);

        handlerCalled.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task ThrowsValidationException_WhenValidatorFails()
    {
        var failingValidator = new StubFailingValidator();
        var behavior = new ValidationBehavior<TestRequest, TestResponse>([failingValidator]);

        Func<Task> act = () => behavior.Handle(
            new TestRequest(),
            () => Task.FromResult(new TestResponse("ok")),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class StubFailingValidator : AbstractValidator<TestRequest>
    {
        public StubFailingValidator()
        {
            RuleFor(x => x).Must(_ => false).WithMessage("always fails");
        }
    }
}
