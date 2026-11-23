using System.Collections.Concurrent;
using LazyTax.Platform.Context;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LazyTax.Kernel.Tests;

public sealed record Ping(string Key);

/// <summary>Records each delivery; fails while <see cref="FailFor"/> holds the key.</summary>
public sealed class PingCounter : IOutboxSubscriber<Ping>
{
  public static readonly ConcurrentDictionary<string, int> Seen = new();
  public static readonly ConcurrentDictionary<string, int> FailFor = new();

  public Task Handle(OutboxEvent meta, Ping payload, CancellationToken ct)
  {
    if (FailFor.TryGetValue(payload.Key, out var left) && left > 0)
    {
      FailFor[payload.Key] = left - 1;
      throw new InvalidOperationException("planned failure");
    }
    Seen.AddOrUpdate(payload.Key, 1, (_, n) => n + 1);
    return Task.CompletedTask;
  }
}

public sealed class PingAudit : IOutboxSubscriber<Ping>
{
  public static readonly ConcurrentDictionary<string, int> Seen = new();

  public Task Handle(OutboxEvent meta, Ping payload, CancellationToken ct)
  {
    Seen.AddOrUpdate(payload.Key, 1, (_, n) => n + 1);
    return Task.CompletedTask;
  }
}

/// <summary>The app plus two test subscribers of "test.ping", retrying at once (backoff 0), at most 3 attempts.</summary>
public sealed class OutboxApp : KernelApp
{
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    base.ConfigureWebHost(builder);
    builder.UseSetting("Outbox:BackoffBaseSeconds", "0");
    builder.UseSetting("Outbox:MaxAttempts", "3");
  }

  protected override void ConfigureTestServices(IServiceCollection services)
  {
    services.AddScoped<PingCounter>();
    services.AddScoped<PingAudit>();
    services.AddSingleton<IReadOnlyList<SubscriptionDefinition>>(
    [
      new SubscriptionDefinition("test.ping", "test:PingCounter", typeof(Ping), typeof(PingCounter)),
      new SubscriptionDefinition("test.ping", "test:PingAudit", typeof(Ping), typeof(PingAudit)),
    ]);
  }
}

public sealed class OutboxTests(OutboxApp app) : IClassFixture<OutboxApp>
{
  private Task Emit(string key, bool fail = false) => app.InScope(sp =>
  {
    var uow = sp.GetRequiredService<IUnitOfWork>();
    var outbox = sp.GetRequiredService<IOutbox>();
    return uow.Start(async () =>
    {
      await outbox.Add("test.ping", new Ping(key));
      if (fail) throw new InvalidOperationException("roll back");
    });
  });

  private Task<long> Row(string key, string column) =>
    app.Count($"SELECT coalesce(max({column}), 0)::bigint FROM kernel.outbox_events WHERE payload->>'key' = @k", ("k", key));

  [Fact]
  public async Task Committed_event_reaches_every_subscriber_once()
  {
    var key = Guid.NewGuid().ToString("N");
    await this.Emit(key);
    await app.DispatchOutbox();
    await app.DispatchOutbox();
    PingCounter.Seen[key].Should().Be(1);
    PingAudit.Seen[key].Should().Be(1);
    (await app.Count("SELECT count(*) FROM kernel.outbox_deliveries d JOIN kernel.outbox_events e ON e.id = d.event_id WHERE e.payload->>'key' = @k", ("k", key)))
      .Should().Be(2);
  }

  [Fact]
  public async Task Rolled_back_event_never_exists()
  {
    var key = Guid.NewGuid().ToString("N");
    var act = () => this.Emit(key, fail: true);
    await act.Should().ThrowAsync<InvalidOperationException>();
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE payload->>'key' = @k", ("k", key))).Should().Be(0);
  }

  [Fact]
  public async Task A_failing_subscriber_retries_without_redelivering_to_the_other()
  {
    var key = Guid.NewGuid().ToString("N");
    PingCounter.FailFor[key] = 1;
    await this.Emit(key);
    await app.DispatchOutbox();                   // counter fails, audit delivered
    PingAudit.Seen[key].Should().Be(1);
    PingCounter.Seen.ContainsKey(key).Should().BeFalse();
    (await this.Row(key, "attempts")).Should().Be(1);

    await app.DispatchOutbox();                   // retry: counter succeeds, audit not redelivered
    PingCounter.Seen[key].Should().Be(1);
    PingAudit.Seen[key].Should().Be(1);
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE payload->>'key' = @k AND dispatched_at IS NOT NULL", ("k", key)))
      .Should().Be(1);
  }

  [Fact]
  public async Task Attempts_are_capped()
  {
    var key = Guid.NewGuid().ToString("N");
    PingCounter.FailFor[key] = 100;
    await this.Emit(key);
    for (var i = 0; i < 6; i++) await app.DispatchOutbox();
    (await this.Row(key, "attempts")).Should().Be(3);
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE payload->>'key' = @k AND dispatched_at IS NULL AND last_error LIKE '%planned failure%'", ("k", key)))
      .Should().Be(1);
  }

  [Fact]
  public async Task Add_outside_a_unit_is_refused()
  {
    var act = () => app.InScope(sp => sp.GetRequiredService<IOutbox>().Add("test.ping", new Ping("x")));
    await act.Should().ThrowAsync<InvalidOperationException>();
  }
}
