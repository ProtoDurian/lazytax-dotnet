using System.Transactions;
using Ctx.Account.Data;
using Ctx.Vacation.Data;
using LazyTax.Platform.Data;
using LazyTax.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LazyTax.Kernel.Tests;

/// <summary>Two contexts' DbContexts, one unit of work: one connection, one transaction, no promotion.</summary>
public sealed class CrossContextTransactionTests(KernelApp app)
{
  private static void Write(IServiceProvider sp, string user)
  {
    sp.GetRequiredService<AccountDb>().Users.Add(new UserRow { Id = user, Username = "n" + user, Email = "e" });
    sp.GetRequiredService<VacationDb>().VacationPeriods.Add(new VacationPeriodRow
    {
      Id = Guid.NewGuid(),
      UserId = user,
      StartDate = new DateOnly(2099, 1, 1),
      EndDate = new DateOnly(2099, 1, 2),
      Timezone = "UTC",
    });
  }

  private Task<long> Rows(string user) => app.Count(
    "SELECT (SELECT count(*) FROM account.\"Users\" WHERE \"Id\" = @u) + (SELECT count(*) FROM vacation.\"VacationPeriods\" WHERE \"UserId\" = @u)",
    ("u", user));

  [Fact]
  public async Task Writes_of_two_contexts_commit_together_without_promotion()
  {
    var user = TestTokens.NewUser();
    await app.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(async () =>
    {
      Write(sp, user);
      await sp.GetRequiredService<AccountDb>().SaveChangesAsync();
      await sp.GetRequiredService<VacationDb>().SaveChangesAsync();
      // Both DbContexts sit on the scope's one connection, so the transaction stays local.
      sp.GetRequiredService<AccountDb>().Database.GetDbConnection()
        .Should().BeSameAs(sp.GetRequiredService<VacationDb>().Database.GetDbConnection());
      Transaction.Current!.TransactionInformation.DistributedIdentifier.Should().Be(Guid.Empty);
    }));
    (await this.Rows(user)).Should().Be(2);
  }

  [Fact]
  public async Task Writes_of_two_contexts_roll_back_together()
  {
    var user = TestTokens.NewUser();
    var act = () => app.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(async () =>
    {
      Write(sp, user);
      await sp.GetRequiredService<AccountDb>().SaveChangesAsync();
      await sp.GetRequiredService<VacationDb>().SaveChangesAsync();
      throw new InvalidOperationException("boom after both writes");
    }));
    await act.Should().ThrowAsync<InvalidOperationException>();
    (await this.Rows(user)).Should().Be(0);
  }

  [Fact]
  public async Task A_nested_unit_joins_the_outer_one()
  {
    var user = TestTokens.NewUser();
    var act = () => app.InScope(sp =>
    {
      var uow = sp.GetRequiredService<IUnitOfWork>();
      return uow.Start(async () =>
      {
        await uow.Start(async () =>
        {
          Write(sp, user);
          await sp.GetRequiredService<AccountDb>().SaveChangesAsync();
          await sp.GetRequiredService<VacationDb>().SaveChangesAsync();
        });
        throw new InvalidOperationException("outer fails after the inner unit returned");
      });
    });
    await act.Should().ThrowAsync<InvalidOperationException>();
    (await this.Rows(user)).Should().Be(0);
  }
}
