using System.ComponentModel.DataAnnotations;

namespace Ctx.Account.Data;

/// <summary>LazyTax's UserData: the JWT sub is the id.</summary>
public sealed class UserRow
{
  [MaxLength(128)] public string Id { get; set; } = string.Empty;

  [MaxLength(256)] public string Username { get; set; } = string.Empty;

  [MaxLength(256)] public string Email { get; set; } = string.Empty;

  public bool EmailVerified { get; set; }

  public string[] Scopes { get; set; } = [];

  public bool Active { get; set; }
}

/// <summary>LazyTax's ConfigurationData. DefaultCharityId points into the charity context: no foreign key.</summary>
public sealed class ConfigurationRow
{
  public Guid Id { get; set; }

  [MaxLength(128)] public string UserId { get; set; } = string.Empty;

  [MaxLength(64)] public string Timezone { get; set; } = "UTC";

  public TimeOnly EndOfDay { get; set; } = new(23, 59);

  public Guid? DefaultCharityId { get; set; }
}
