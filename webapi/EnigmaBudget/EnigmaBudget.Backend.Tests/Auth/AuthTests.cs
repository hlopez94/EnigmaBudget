using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Infrastructure.Auth.Helpers;
namespace EnigmaBudget.Backend.Tests.Auth
{
    public class Tests
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

            string password = "Password1234";
            string saltA = HashHelper.CreateSalt();
            string saltB = HashHelper.CreateSalt();
            var encryptedPass = HashHelper.HashPassword(password, saltA);
            var newEncryptedPass = HashHelper.HashPassword(password, saltB);

            Assert.That(saltA, !Is.EqualTo(saltB));
            Assert.That(encryptedPass, !Is.EqualTo(newEncryptedPass));
        }
    }
}