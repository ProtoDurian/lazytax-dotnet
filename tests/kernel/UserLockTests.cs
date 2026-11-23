using LazyTax.Platform.Data;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LazyTax.Kernel.Tests;

/// <summary>Two overlapping "lock, count, then write" units on one user write exactly one row.</summary>
public sealed class UserLockTests(KernelApp app)
{
  private async Task Table()
  {
    await using var c = new NpgsqlConnection(app.ConnectionString);
    await c.OpenAsync();
    await using var cmd = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS public.lock_probe (user_id text NOT NULL, n int NOT NULL)", c);
    await cmd.ExecuteNonQueryAsync();
  }

  private Task CountThenWrite(string user, UnitIsolation isolation, TimeSpan hold) => app.InScope(async sp =>
  {
    var uow = sp.GetRequiredService<IUnitOfWork>();
    var shared = sp.GetRequiredService<SharedConnection>();
    await uow.Start(isolation, async () =>
    {
      await uow.LockUser(user);
      await using var count = new NpgsqlCommand("SELECT count(*) FROM public.lock_probe WHERE user_id = @u", shared.Connection);
      count.Parameters.AddWithValue("u", user);
      var n = (long)(await count.ExecuteScalarAsync())!;
      await Task.Delay(hold); // keep the lock so the other unit waits on it
      if (n >= 1) return;     // quota of one
      await using var insert = new NpgsqlCommand("INSERT INTO public.lock_probe (user_id, n) VALUES (@u, 1)", shared.Connection);
      insert.Parameters.AddWithValue("u", user);
      await insert.ExecuteNonQueryAsync();
    });
  });

  [Theory]
  [InlineData(UnitIsolation.ReadCommitted)]
  [InlineData(UnitIsolation.RepeatableRead)]
  public async Task Overlapping_units_write_exactly_one_row(UnitIsolation isolation)
  {
    await this.Table();
    var user = TestTokens.NewUser();
    await Task.WhenAll(
      this.CountThenWrite(user, isolation, TimeSpan.FromMilliseconds(400)),
      this.CountThenWrite(user, isolation, TimeSpan.FromMilliseconds(400)),
      this.CountThenWrite(user, isolation, TimeSpan.FromMilliseconds(400)));
    (await app.Count("SELECT count(*) FROM public.lock_probe WHERE user_id = @u", ("u", user))).Should().Be(1);
  }

  [Fact]
  public async Task LockUser_outside_a_unit_is_refused()
  {
    var act = () => app.InScope(sp => sp.GetRequiredService<IUnitOfWork>().LockUser("x"));
    await act.Should().ThrowAsync<InvalidOperationException>();
  }
}
