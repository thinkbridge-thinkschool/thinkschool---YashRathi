using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;

namespace QuotesApi.Authorization;

// Marker — carries no data; the handler does all the work.
public class OwnerRequirement : IAuthorizationRequirement { }

// Resource-based handler: succeeds only when the JWT sub matches OwnerId on the quote.
// Works with MapInboundClaims = false so claim types stay as raw JWT names.
public class QuoteOwnerAuthorizationHandler
    : AuthorizationHandler<OwnerRequirement, Quote>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerRequirement requirement,
        Quote resource)
    {
        var userId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (userId is not null && userId == resource.OwnerId)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
