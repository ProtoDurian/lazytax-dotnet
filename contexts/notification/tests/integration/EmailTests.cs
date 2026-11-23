using System.Text.Json;
using Ctx.Account.Contract.V1;
using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Testing;
using LazyTax.Kernel.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Notification.Tests.Integration;

/// <summary>Email and the penalty events, on the real outbox, with a fake account (so they do not wait for the real one) and a private mail folder.</summary>
public sealed class EmailTests : IDisposable
{
  private sealed class FakeAccount : IAccountV1
  {
    public Task<AccountProfile?> GetProfile(Owner owner, CancellationToken ct = default) =>
      Task.FromResult<AccountProfile?>(owner.UserId!.StartsWith("nomail") ? null
        : new AccountProfile(owner.UserId!, "Sarah", owner.UserId + "@example.com", true, true));
    public Task<bool> Exists(string userId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string?> GetTimezone(string userId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TimeOnly> EndOfDay(string userId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Guid?> DefaultCharity(string userId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UserToday> Today(string userId, DateTime? nowUtc = null, CancellationToken ct = default) => throw new NotImplementedException();
  }

  private readonly string mailDir = Path.Combine(Path.GetTempPath(), "lt-mail-" + Guid.NewGuid().ToString("N")[..8]);
  private readonly KernelApp root;
  private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> web;

  public EmailTests(KernelApp app)
  {
    this.root = app;
    this.web = app.WithWebHostBuilder(b =>
    {
      b.UseSetting("Contexts:notification:MailDir", this.mailDir);
      b.ConfigureServices(s => s.AddScoped<IAccountV1, FakeAccount>());
    });
  }

  public void Dispose()
  {
    this.web.Dispose();
    if (Directory.Exists(this.mailDir)) Directory.Delete(this.mailDir, true);
  }

  private async Task InScope(Func<IServiceProvider, Task> work)
  {
    await using var scope = this.web.Services.CreateAsyncScope();
    await work(scope.ServiceProvider);
  }

  /// <summary>Delivers until nothing is due: a subscriber can add events of its own (penalty.charged adds an email request).</summary>
  private async Task Dispatch()
  {
    var dispatcher = this.web.Services.GetRequiredService<IOutboxDispatcher>();
    for (var i = 0; i < 5 && await dispatcher.DispatchOnce() > 0; i++) { }
  }

  private JsonElement[] Mails() => !Directory.Exists(this.mailDir)
    ? []
    : Directory.GetFiles(this.mailDir, "*.json").Select(f => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(f))).ToArray();

  [Fact]
  public async Task Email_is_sent_after_commit_not_before()
  {
    var u = "u_" + Guid.NewGuid().ToString("N")[..10];
    await this.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(async () =>
    {
      await sp.GetRequiredService<INotificationV1>().Email(u, EmailTemplateIds.PenaltyCharged, new Dictionary<string, string>
      {
        ["Amount"] = "$5.00",
        ["CharityName"] = "Red Cross",
        ["HabitName"] = "Run",
        ["ChargeDate"] = "1 January 2026",
      });
      this.Mails().Should().BeEmpty("nothing leaves inside the unit of work");
    }));

    this.Mails().Should().BeEmpty("the dispatcher has not run");
    await this.Dispatch();
    var mail = this.Mails().Where(m => m.GetProperty("To").GetString() == u + "@example.com").Single();
    mail.GetProperty("Subject").GetString().Should().Be("Your LazyTax habit stake receipt");
    mail.GetProperty("Html").GetString().Should().Contain("Hi Sarah").And.Contain("$5.00").And.Contain("Red Cross").And.NotContain("{{");
  }

  [Fact]
  public async Task A_rolled_back_unit_sends_nothing()
  {
    var u = "u_" + Guid.NewGuid().ToString("N")[..10];
    var act = () => this.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(async () =>
    {
      await sp.GetRequiredService<INotificationV1>().Email(u, EmailTemplateIds.Welcome);
      throw new InvalidOperationException("boom");
    }));
    await act.Should().ThrowAsync<InvalidOperationException>();
    await this.Dispatch();
    this.Mails().Should().NotContain(m => m.GetProperty("To").GetString() == u + "@example.com");
  }

  [Fact]
  public async Task A_missing_variable_or_address_is_skipped_without_failing()
  {
    var noMail = "nomail_" + Guid.NewGuid().ToString("N")[..8];
    var u = "u_" + Guid.NewGuid().ToString("N")[..10];
    await this.InScope(async sp =>
    {
      var n = sp.GetRequiredService<INotificationV1>();
      await n.Email(noMail, EmailTemplateIds.Welcome);            // no address
      await n.Email(u, EmailTemplateIds.PenaltyCharged);          // variables missing
    });
    var act = () => this.Dispatch();
    await act.Should().NotThrowAsync();
    this.Mails().Should().BeEmpty();
    (await this.web.Services.GetRequiredService<IOutboxDispatcher>().DispatchOnce()).Should().Be(0, "no retry loop: both were consumed");
  }

  [Fact]
  public async Task An_unknown_template_is_400()
  {
    var act = () => this.InScope(sp => sp.GetRequiredService<INotificationV1>().Email("u", "nope"));
    await act.Should().ThrowAsync<LazyTax.Platform.Problems.ValidationFailed>();
  }

  [Fact]
  public async Task Penalty_charged_gives_an_inbox_row_and_an_email_once()
  {
    var u = "u_" + Guid.NewGuid().ToString("N")[..10];
    await this.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(() => sp.GetRequiredService<IOutbox>().Add("penalty.charged", new
    {
      penaltyId = Guid.NewGuid(),
      userId = u,
      habitId = Guid.NewGuid(),
      habitExecutionId = Guid.NewGuid(),
      charityId = Guid.NewGuid(),
      charityName = "Red Cross",
      amountCents = 1250,
      currency = "USD",
      paymentIntentId = "pi_1",
      chargedAtUtc = new DateTime(2026, 7, 12, 8, 0, 0, DateTimeKind.Utc),
    })));
    await this.Dispatch();
    await this.Dispatch();

    var inbox = await this.web.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope().ServiceProvider
      .GetRequiredService<INotificationV1>().UnreadCount(u);
    inbox.Should().Be(1);
    var mail = this.Mails().Where(m => m.GetProperty("To").GetString() == u + "@example.com").Single();
    mail.GetProperty("Html").GetString().Should().Contain("$12.50").And.Contain("Red Cross").And.Contain("12 July 2026");
  }

  [Fact]
  public async Task Penalty_failed_gives_an_inbox_row_and_an_email()
  {
    var u = "u_" + Guid.NewGuid().ToString("N")[..10];
    await this.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(() => sp.GetRequiredService<IOutbox>().Add("penalty.failed", new
    {
      penaltyId = Guid.NewGuid(),
      userId = u,
      habitId = Guid.NewGuid(),
      habitExecutionId = Guid.NewGuid(),
      charityId = Guid.NewGuid(),
      amountCents = 500,
      currency = "EUR",
      error = "declined",
      attemptedAtUtc = new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc),
    })));
    await this.Dispatch();
    var mail = this.Mails().Where(m => m.GetProperty("To").GetString() == u + "@example.com").Single();
    mail.GetProperty("Subject").GetString().Should().Be("We couldn't process your stake");
    mail.GetProperty("Html").GetString().Should().Contain("5.00 EUR").And.Contain("your chosen charity");
  }
}
