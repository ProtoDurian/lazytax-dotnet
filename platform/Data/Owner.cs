using System.Linq.Expressions;

namespace LazyTax.Platform.Data;

/// <summary>
/// Who a repository read is scoped to (auth layer 4). Every repository read takes one; never a nullable
/// user id where null means "everyone" (the nitroso <c>BookingRepository.cs:373</c> anti-pattern).
/// <see cref="Anyone"/> is for admin-only routes and workers, and must be spelled out.
/// </summary>
public readonly record struct Owner
{
  private Owner(string? userId) => this.UserId = userId;

  /// <summary>The user the rows must belong to; null only for <see cref="Anyone"/>.</summary>
  public string? UserId { get; }

  public bool IsAnyone => this.UserId is null;

  public static Owner User(string userId)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(userId);
    return new Owner(userId);
  }

  /// <summary>No owner filter. Admin routes and trusted workers only.</summary>
  public static Owner Anyone() => new(null);

  public override string ToString() => this.UserId ?? "<anyone>";
}

public static class OwnerQueryExtensions
{
  /// <summary>
  /// Filter by owner. <paramref name="userIdOf"/> may walk foreign keys (<c>v => v.Habit.UserId</c>); EF turns
  /// that into a JOIN. 0 rows means 404, never 403, so nothing leaks.
  /// </summary>
  public static IQueryable<T> OwnedBy<T>(this IQueryable<T> query, Owner owner, Expression<Func<T, string>> userIdOf)
  {
    if (owner.IsAnyone) return query;
    var holder = new OwnerHolder(owner.UserId!);
    var body = Expression.Equal(userIdOf.Body,
      Expression.Property(Expression.Constant(holder), nameof(OwnerHolder.UserId)));
    return query.Where(Expression.Lambda<Func<T, bool>>(body, userIdOf.Parameters));
  }

  private sealed record OwnerHolder(string UserId);
}
