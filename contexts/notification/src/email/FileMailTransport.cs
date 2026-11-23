using System.Text.Json;
using Ctx.Notification.Domain;

namespace Ctx.Notification.Email;

/// <summary>
/// The fake SMTP adapter: one JSON file per message in <c>Contexts:notification:MailDir</c> (default: the OS
/// temp folder, <c>lazytax-mail</c>). No network. The file is named by the message id, so a redelivered
/// outbox event overwrites its own file and leaves one message.
/// </summary>
public sealed class FileMailTransport(string directory) : IMailTransport
{
  public string Directory { get; } = directory;

  public async Task Send(MailMessage message, CancellationToken ct)
  {
    System.IO.Directory.CreateDirectory(this.Directory);
    var path = Path.Combine(this.Directory, $"{message.MessageId}.json");
    var temp = path + ".tmp";
    await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(message), ct);
    File.Move(temp, path, overwrite: true);
  }
}
