using System.Security.Cryptography;

namespace Tsdt.Api.Identity;

public sealed class TemporaryPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*-_+";
    private const string All = Uppercase + Lowercase + Digits + Symbols;

    public string Generate()
    {
        Span<char> password = stackalloc char[16];
        password[0] = Pick(Uppercase);
        password[1] = Pick(Lowercase);
        password[2] = Pick(Digits);
        password[3] = Pick(Symbols);
        for (var index = 4; index < password.Length; index++) password[index] = Pick(All);

        for (var index = password.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (password[index], password[swapIndex]) = (password[swapIndex], password[index]);
        }
        return new string(password);
    }

    private static char Pick(string characters) => characters[RandomNumberGenerator.GetInt32(characters.Length)];
}
