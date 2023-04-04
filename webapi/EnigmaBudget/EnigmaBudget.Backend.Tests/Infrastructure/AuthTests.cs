using EnigmaBudget.Infrastructure.Auth.Helpers;
namespace EnigmaBudget.Backend.Tests.Auth
{
    public class AuthTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void PasswordHashDoesntChange()
        {
            string password = "Password1234";
            string salt = HashHelper.CreateSalt();
            var encryptedPass = HashHelper.HashPassword(password, salt);
            var newEncryptedPass = HashHelper.HashPassword(password, salt);

            Assert.That(newEncryptedPass, Is.EqualTo(encryptedPass));
        }

        [Test]
        public void SaltsDifferBetweenCreations()
        {

            string saltA = HashHelper.CreateSalt();
            string saltB = HashHelper.CreateSalt();

            Assert.That(saltA, Is.Not.EqualTo(saltB));
        }

        [Test]
        public void HashingAPasswordWithDifferentSaltsDiffer()
        {

            string password = "Password1234";
            var encryptedPass = HashHelper.HashPassword(password, HashHelper.CreateSalt());
            var newEncryptedPass = HashHelper.HashPassword(password, HashHelper.CreateSalt());

            Assert.That(encryptedPass, Is.Not.EqualTo(newEncryptedPass));
        }

        [Test]
        [TestCase("password1234", "saltA", "password1234", true)]
        [TestCase("password1234", "saltA", "password1235", false)]
        [TestCase("password1234", "saltB", "password1234", true)]
        public void ValidatesPasswords(string originalPassword, string salt, string sentPassword, bool areEqual)
        {
            var hashedPassword = HashHelper.HashPassword(originalPassword, salt);
            Assert.IsTrue(areEqual.Equals(HashHelper.HashedPasswordIsValid(hashedPassword, sentPassword, salt)));
        }
    }
}