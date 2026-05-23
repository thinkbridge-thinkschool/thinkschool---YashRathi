using FluentAssertions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class ResultTests
{
    [Fact]
    public void Ok_IsSuccessIsTrue()
    {
        // Act
        var result = Result<string>.Ok("hello");

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Ok_ValueMatchesInput()
    {
        // Act
        var result = Result<int>.Ok(42);

        // Assert
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Ok_ErrorIsNull()
    {
        // Act
        var result = Result<string>.Ok("hello");

        // Assert
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_IsSuccessIsFalse()
    {
        // Act
        var result = Result<string>.Fail("Something went wrong");

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Fail_ErrorMessageMatchesInput()
    {
        // Arrange
        var message = "Validation failed";

        // Act
        var result = Result<string>.Fail(message);

        // Assert
        result.Error!.Message.Should().Be(message);
    }

    [Fact]
    public void Fail_ValueIsNull()
    {
        // Act
        var result = Result<string>.Fail("error");

        // Assert
        result.Value.Should().BeNull();
    }
}
