using IviCli.Domain;

namespace IviCli.Domain.Tests;

public class ResultTests
{
    [Fact]
    public void Success_StoresValue()
    {
        // Given / When
        var result = Result.Success<int, string>(42);

        // Then
        var ok = result.ShouldBeOfType<Result<int, string>.Ok>();
        ok.Value.ShouldBe(42);
    }

    [Fact]
    public void Failure_StoresError()
    {
        // Given / When
        var result = Result.Failure<int, string>("oops");

        // Then
        var err = result.ShouldBeOfType<Result<int, string>.Error>();
        err.Err.ShouldBe("oops");
    }

    [Fact]
    public void Ok_AndError_ForSameTypesAreNotEqual()
    {
        // Given
        Result<int, string> ok = Result.Success<int, string>(1);
        Result<int, string> err = Result.Failure<int, string>("e");

        // When / Then
        ok.ShouldNotBe(err);
    }

    [Fact]
    public void Ok_WithSameValueAreEqual()
    {
        // Given
        Result<int, string> a = Result.Success<int, string>(1);
        Result<int, string> b = Result.Success<int, string>(1);

        // When / Then
        a.ShouldBe(b);
    }
}
