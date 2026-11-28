using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scolia.Application.Abstractions;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

public sealed class ManagementModeCutoverTests
{
    private sealed class Pause
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PausedGate(WriteGate inner, IHttpContextAccessor context, Pause pause) : IWriteGate
    {
        public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (context.HttpContext?.Request is { Method: "POST", Path.Value: "/api/paiements" })
            {
                pause.Entered.TrySetResult();
                await pause.Release.Task.WaitAsync(cancellationToken);
            }
            return await inner.RunAsync(operation, cancellationToken);
        }
    }

    [Fact]
    public async Task A_payment_authorized_before_the_change_cannot_write_after_the_cutover()
    {
        using var app = new ScoliaApp();
        var pause = new Pause();
        using var factory = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(pause);
            services.AddScoped<WriteGate>();
            services.RemoveAll<IWriteGate>();
            services.AddScoped<IWriteGate, PausedGate>();
        }));
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        admin.DefaultRequestHeaders.Add("X-Scolia", "1");
        await admin.Post("/api/installation", new
        {
            schoolName = "École transition", shortName = "TR", city = "Lubumbashi", phone = "0810000000", currencyCode = "CDF", currencySymbol = "FC",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)), startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Secrétariat", adminPassword = "Scolia2026!", loadDemoData = true
        }).Ok();
        await admin.Post("/api/utilisateurs", new { userName = "compta-transition", displayName = "Comptable", role = "Comptabilite", password = "Provisoire1" }).Ok();
        var enrollment = (await admin.GetAsync("/api/apprenants?taille=1").Ok()).GetProperty("items")[0].GetProperty("enrollmentId").GetString()!;
        var months = await admin.OpenMonths(enrollment, 1);
        int before;
        using (var scope = factory.Services.CreateScope()) before = await scope.ServiceProvider.GetRequiredService<ScoliaDbContext>().Receipts.CountAsync();
        var pending = admin.Post("/api/paiements", new { enrollmentId = enrollment, amount = months.Total, feeItemIds = months.Ids, method = "Especes" });
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await admin.Put("/api/organisation/gestion", new { mode = "Separee", currentPassword = "Scolia2026!" }).Ok();
        }
        finally { pause.Release.TrySetResult(); }
        using var refused = await pending;
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var afterScope = factory.Services.CreateScope();
        Assert.Equal(before, await afterScope.ServiceProvider.GetRequiredService<ScoliaDbContext>().Receipts.CountAsync());
    }
}
