using System.Security.Cryptography;
using System.Text;

namespace Ctx.Proof.Domain;

/// <summary>AES-256-GCM for Strava tokens at rest. The key is the SHA-256 of <c>strava.encryptionKey</c>.</summary>
public sealed class TokenBox(ProofOptions options)
{
  private const int NonceSize = 12;
  private const int TagSize = 16;

  private byte[] Key => SHA256.HashData(Encoding.UTF8.GetBytes(options.Strava.EncryptionKey));

  public string Encrypt(string plain)
  {
    var nonce = RandomNumberGenerator.GetBytes(NonceSize);
    var data = Encoding.UTF8.GetBytes(plain);
    var cipher = new byte[data.Length];
    var tag = new byte[TagSize];
    using var aes = new AesGcm(this.Key, TagSize);
    aes.Encrypt(nonce, data, cipher, tag);
    return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
  }

  public string Decrypt(string boxed)
  {
    var all = Convert.FromBase64String(boxed);
    var nonce = all[..NonceSize];
    var tag = all[NonceSize..(NonceSize + TagSize)];
    var cipher = all[(NonceSize + TagSize)..];
    var data = new byte[cipher.Length];
    using var aes = new AesGcm(this.Key, TagSize);
    aes.Decrypt(nonce, cipher, tag, data);
    return Encoding.UTF8.GetString(data);
  }
}
