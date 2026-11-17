using AtelieMarylu.Web.Components.Account;
using Microsoft.AspNetCore.Identity;

namespace AtelieMarylu.Tests;

// O cadastro não pode mostrar "e-mail já cadastrado" (enumeração de contas), mas os outros erros sim
public class RegistrationErrorsTests
{
    private static readonly IdentityErrorDescriber Describer = new();

    [Fact]
    public void Duplicate_email_and_username_are_hidden()
    {
        var (emailTaken, others) = RegistrationErrors.Split(
        [
            Describer.DuplicateUserName("maria@teste.local"),
            Describer.DuplicateEmail("maria@teste.local"),
        ]);

        Assert.True(emailTaken);
        Assert.Empty(others);
    }

    [Fact]
    public void Other_errors_are_kept_and_do_not_mention_the_email()
    {
        var (emailTaken, others) = RegistrationErrors.Split(
        [
            Describer.DuplicateEmail("maria@teste.local"),
            Describer.PasswordTooShort(8),
        ]);

        Assert.True(emailTaken);
        var error = Assert.Single(others);
        Assert.Equal(nameof(IdentityErrorDescriber.PasswordTooShort), error.Code);
        Assert.DoesNotContain("maria@teste.local", error.Description);
    }

    [Fact]
    public void New_email_is_not_reported_as_taken()
    {
        var (emailTaken, others) = RegistrationErrors.Split([Describer.PasswordRequiresDigit()]);

        Assert.False(emailTaken);
        Assert.Single(others);
    }
}
