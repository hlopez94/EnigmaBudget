using System.Security.Cryptography;
using System.Text;

namespace EnigmaBudget.Infrastructure.Auth.Helpers
{
    public static class HashHelper
    {
        /// <summary>
        /// Hashes a password given a salt string 
        /// </summary>
        /// <param name="password">User Password to hash</param>
        /// <param name="salt">Salt string to append to the password for extra security</param>
        /// <returns>A string containing the hashed salted password</returns>
        public static string HashPassword(this string password, string salt)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(password + salt);

            SHA256.Create();
            SHA256 sHA256ManagedString = SHA256.Create();
            byte[] hash = sHA256ManagedString.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
        /// <summary>
        /// Creates an N bytes size random string given a bytes value. 16bytes by default.
        /// </summary>
        /// <param name="sizeInBytes">Size of the random string to create in bytes</param>
        /// <returns>Random string</returns>
        public static string CreateSalt(int sizeInBytes = 16)
        {
            //Generate a cryptographic random number.
            RandomNumberGenerator rng = RandomNumberGenerator.Create();
            byte[] buff = new byte[sizeInBytes];
            rng.GetBytes(buff);
            return Convert.ToBase64String(buff);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="hashedPassword"></param>
        /// <param name="plainTextPassword"></param>
        /// <param name="salt"></param>
        /// <returns></returns>
        public static bool HashedPasswordIsValid(this string hashedPassword, string plainTextPassword, string salt)
        {
            string newHashedPin = plainTextPassword.HashPassword(salt);
            return newHashedPin.Equals(hashedPassword);
        }
    }
}
