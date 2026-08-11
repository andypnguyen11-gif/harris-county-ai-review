using HarrisCountyAI.Api.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HarrisCountyAI.UnitTests.Authentication;

public class DevTokenServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static DevTokenService CreateService(ApiAuthenticationOptions? options = null)
    {
        options ??= CreateOptions();
        return new DevTokenService(Options.Create(options), new FixedTimeProvider(FixedNow));
    }

    private static ApiAuthenticationOptions CreateOptions() => new()
    {
        Mode = AuthenticationModes.LocalDevelopment,
        LocalDevelopment = new LocalDevelopmentAuthenticationOptions
        {
            Issuer = "UnitTest.Issuer",
            Audience = "UnitTest.Audience",
            SigningKey = "unit-test-signing-key-0123456789-0123456789",
            TokenLifetimeMinutes = 30,
            Users =
            [
                new DevelopmentUser
                {
                    Username = "dev.reviewer",
                    DisplayName = "Dev Reviewer",
                    Roles = ["Reviewer"],
                },
                new DevelopmentUser
                {
                    Username = "dev.admin",
                    DisplayName = "Dev Administrator",
                    Roles = ["Administrator"],
                },
            ],
        },
    };

    [Fact]
    public void IssueToken_Returns_Null_For_Unknown_User()
    {
        var token = CreateService().IssueToken("not.a.user");

        Assert.Null(token);
    }

    [Fact]
    public void IssueToken_Matches_Username_Case_Insensitively()
    {
        var token = CreateService().IssueToken("DEV.Reviewer");

        Assert.NotNull(token);
        Assert.Equal("dev.reviewer", token.Username);
    }

    [Fact]
    public void IssueToken_Reports_User_Metadata_And_Expiry()
    {
        var token = CreateService().IssueToken("dev.admin");

        Assert.NotNull(token);
        Assert.Equal("dev.admin", token.Username);
        Assert.Equal("Dev Administrator", token.DisplayName);
        Assert.Equal(["Administrator"], token.Roles);
        Assert.Equal(FixedNow.AddMinutes(30), token.ExpiresAt);
    }

    [Fact]
    public void IssueToken_Embeds_Issuer_Audience_Name_And_Role_Claims()
    {
        var token = CreateService().IssueToken("dev.reviewer");

        Assert.NotNull(token);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.AccessToken);

        Assert.Equal("UnitTest.Issuer", jwt.Issuer);
        Assert.Equal(["UnitTest.Audience"], jwt.Audiences);
        Assert.Equal("dev.reviewer", jwt.Subject);
        Assert.Equal("Dev Reviewer", jwt.GetClaim("name").Value);
        Assert.Equal("dev.reviewer", jwt.GetClaim("preferred_username").Value);
        Assert.Equal(["Reviewer"], jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value).ToArray());
        Assert.Equal(FixedNow.AddMinutes(30).UtcDateTime, jwt.ValidTo);
    }

    [Fact]
    public void IssueToken_Includes_Every_Configured_Role()
    {
        var options = CreateOptions();
        options.LocalDevelopment.Users[0].Roles = ["Reviewer", "Administrator"];

        var token = CreateService(options).IssueToken("dev.reviewer");

        Assert.NotNull(token);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.AccessToken);
        var roles = jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value).ToArray();

        Assert.Equal(["Reviewer", "Administrator"], roles);
    }

    [Fact]
    public void IssueToken_Falls_Back_To_Username_When_DisplayName_Missing()
    {
        var options = CreateOptions();
        options.LocalDevelopment.Users[0].DisplayName = "";

        var token = CreateService(options).IssueToken("dev.reviewer");

        Assert.NotNull(token);
        Assert.Equal("dev.reviewer", token.DisplayName);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
