using System;
using System.Security.Cryptography;
using System.Text;

namespace RefaccionariaPOS.Security
{
    public static class PasswordHasher
    {
        private const int Iterations = 100000;
        private const int SaltSize = 16;
        private const int KeySize = 32;

        public static string Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        public static bool Verify(string password, string storedHash)
        {
            if (storedHash.StartsWith("PBKDF2$", StringComparison.Ordinal))
            {
                string[] parts = storedHash.Split('$');
                if (parts.Length != 4 || !int.TryParse(parts[1], out int iterations))
                {
                    return false;
                }

                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expected = Convert.FromBase64String(parts[3]);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(storedHash),
                Encoding.UTF8.GetBytes(HashSha256(password)));
        }

        private static string HashSha256(string plainText)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
            StringBuilder builder = new StringBuilder(bytes.Length * 2);

            foreach (byte value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
