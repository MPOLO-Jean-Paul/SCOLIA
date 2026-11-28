using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Scolia.Api.Security;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

public sealed class ManagementModeCookieUpgradeTests
{
    [Fact]
    public async Task An_existing_valid_mixed_cookie_gets_the_new_internal_permissions_without_losing_access()
    {
        using var app = new ScoliaApp();
        using var admin = await app.InstalledAdmin();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "admin");
        var principal = SessionFactory.Principal(user, SchoolManagementMode.Mixte);
        var identity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in identity.Claims.Where(claim => claim.Type is ScoliaClaims.PermissionVersion or ScoliaClaims.ManagementMode
            || claim.Type == ScoliaClaims.Permission && new[] { Permissions.StaffRead, Permissions.PayrollManage,
                Permissions.SponsorshipManage, Permissions.StudentFinanceRead }.Contains(claim.Value)).ToArray()) identity.RemoveClaim(claim);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var cookie = options.TicketDataFormat.Protect(new AuthenticationTicket(principal,
            new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), AllowRefresh = true },
            CookieAuthenticationDefaults.AuthenticationScheme));
        using var legacy = app.Client();
        legacy.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + cookie);
        using var session = await legacy.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.True(session.Headers.TryGetValues("Set-Cookie", out var renewed) && renewed.Any(value => value.StartsWith(options.Cookie.Name + "=", StringComparison.Ordinal)));
        var period = DateTime.Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        await legacy.Post($"/api/personnel/paie/{period}/preparation", new { }).Ok();
    }
}
