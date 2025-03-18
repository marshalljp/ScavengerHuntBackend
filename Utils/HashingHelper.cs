using System.Security.Cryptography;
using System.Text;

namespace ScavengerHuntBackend.Utils
{
    public static class HashingHelper
    {
        public static string HashAnswer(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public static bool VerifyHash(string input, string hash)
        {
            return HashAnswer(input) == hash;
        }
    }
}
