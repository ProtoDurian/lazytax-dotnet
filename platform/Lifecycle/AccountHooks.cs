namespace LazyTax.Platform.Lifecycle;

// Account deletion runs, in this order (account owns the flow; other contexts only register hooks):
//   1. every IDeleteGuard        (any throw blocks deletion; nothing has changed yet)
//   2. every IBeforePurge        (OUTSIDE any transaction, best effort; failures are logged and ignored)
//   3. every IUserPurge          (INSIDE one unit of work, with account's own rows; all or nothing)

/// <summary>Blocks account deletion by throwing a <c>Problem</c> (penalty: outstanding debt, 409).</summary>
public interface IDeleteGuard
{
  Task Check(string userId, CancellationToken ct);
}

/// <summary>Best-effort external clean-up before the purge (payment: revoke consents). Never throws on purpose.</summary>
public interface IBeforePurge
{
  Task Run(string userId, CancellationToken ct);
}

/// <summary>Deletes (or anonymises) every row of the user in this context's schema. Runs inside the purge transaction.</summary>
public interface IUserPurge
{
  Task Purge(string userId, CancellationToken ct);
}
