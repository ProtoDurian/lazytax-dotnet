namespace LazyTax.Platform.Problems;

/// <summary>
/// A domain error. Throw one anywhere (service, repository, guard, filter); the kernel turns it into
/// problem-details JSON with <see cref="Status"/>, <c>type</c> = <c>/problems/v1/&lt;Id&gt;</c>, <see cref="Title"/>,
/// <see cref="Problem.Message"/> as <c>detail</c> and <see cref="Data"/> as <c>data</c>. A context adds its
/// own problems by subclassing; no shared switch to edit. Mapping follows LazyTax's <c>BaseController</c>.
/// </summary>
public abstract class Problem(int status, string id, string title, string detail, object? data = null)
  : Exception(detail)
{
  public int Status { get; } = status;

  /// <summary>Stable snake_case id, e.g. <c>entity_not_found</c>.</summary>
  public string Id { get; } = id;

  public string Title { get; } = title;

  /// <summary>Extra fields for clients; never tokens, emails or bodies.</summary>
  public new object? Data { get; } = data;
}

/// <summary>404. Also what an owner-scoped read returns for another user's row.</summary>
public sealed class NotFound(string what, string id)
  : Problem(404, "entity_not_found", "Entity Not Found", $"{what} Not Found", new { requestIdentifier = id, typeName = what });

/// <summary>400 with per-field errors.</summary>
public sealed class ValidationFailed(string detail, IReadOnlyDictionary<string, string[]> errors)
  : Problem(400, "validation_error", "Validation Error", detail, new { errors })
{
  public ValidationFailed(string detail, string field, string error)
    : this(detail, new Dictionary<string, string[]> { [field] = [error] }) { }
}

/// <summary>401: no or bad token.</summary>
public sealed class Unauthenticated(string detail = "You are not authenticated")
  : Problem(401, "unauthenticated", "Unauthenticated", detail);

/// <summary>403: valid token, not allowed (policy or subject rule).</summary>
public sealed class Forbidden(string detail = "You are not authorized to access this resource")
  : Problem(403, "unauthorized", "Unauthorized", detail);

/// <summary>409.</summary>
public sealed class Conflict(string detail, object? data = null)
  : Problem(409, "entity_conflict", "Entity Conflict", detail, data);

/// <summary>412.</summary>
public sealed class PreconditionFailed(string id, string title, string detail, object? data = null)
  : Problem(412, id, title, detail, data);

/// <summary>409: another request of the same user holds the user lock; retry shortly.</summary>
public sealed class UserBusy(string userId)
  : Problem(409, "user_busy", "User Busy", "Another request of this user is in progress; retry shortly.", new { userId });
