using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;

namespace AutoCore.Database.Auth.Models;

[Table("account")]
public class Account
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public uint Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public byte Level { get; set; }
    public string? LastIP { get; set; }
    public byte LastServerId { get; set; }
    public DateTime? LastLogin { get; set; }
    public DateTime JoinDate { get; set; }
    public bool Locked { get; set; }
    public bool Validated { get; set; }
    public string? ValidationToken { get; set; }

    public bool CheckPassword(string password)
    {
        return Hash(password, Salt) == Password;
    }

    public static string Hash(string password, string salt)
    {
        return BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{password}"))).Replace("-", "").ToLower();
    }

    public static string CreateSalt()
    {
        var salt = new byte[20];

        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(salt);

        return BitConverter.ToString(salt).Replace("-", "").ToLower();
    }
}
