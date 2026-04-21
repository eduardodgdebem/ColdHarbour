using ColdHarbour.Domain.Identity;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Identity;

public class UserTests
{
    private static PasswordHash ValidHash() => PasswordHash.From(new string('a', 64));

    [Fact]
    public void Create_WithValidInputs_SetsAllProperties()
    {
        var hash = ValidHash();

        var user = User.Create("alice@example.com", "Alice", hash, Role.User);

        user.Id.Should().NotBeEmpty();
        user.Email.Should().Be("alice@example.com");
        user.Name.Should().Be("Alice");
        user.PasswordHash.Should().Be(hash);
        user.Role.Should().Be(Role.User);
        user.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_DefaultRole_IsUser()
    {
        var user = User.Create("bob@example.com", "Bob", ValidHash());

        user.Role.Should().Be(Role.User);
    }

    [Fact]
    public void Create_WithOwnerRole_SetsOwner()
    {
        var user = User.Create("owner@example.com", "Owner", ValidHash(), Role.Owner);

        user.Role.Should().Be(Role.Owner);
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithInvalidEmail_Throws(string? email)
    {
        var act = () => User.Create(email!, "Alice", ValidHash());

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithWhitespaceName_Throws(string? name)
    {
        var act = () => User.Create("alice@example.com", name!, ValidHash());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HasRole_ReturnsTrueForMatchingRole()
    {
        var user = User.Create("alice@example.com", "Alice", ValidHash(), Role.Owner);

        user.HasRole(Role.Owner).Should().BeTrue();
        user.HasRole(Role.User).Should().BeFalse();
    }
}
