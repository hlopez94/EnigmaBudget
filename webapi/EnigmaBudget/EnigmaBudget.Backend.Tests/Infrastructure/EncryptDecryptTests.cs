using EnigmaBudget.Infrastructure.Helpers;

namespace EnigmaBudget.Backend.Tests.Infrastructure
{
    public class EncryptDecryptTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            EncodeDecodeHelper.Init("EncriptionKeyForTests");
        }

        [Test]
        public void EncryptingAStringMultipleTimesGeneratesSameValue()
        {

            string plainText = "StringToEncrypt";
            Assert.That(EncodeDecodeHelper.Encrypt(plainText), Is.EqualTo(EncodeDecodeHelper.Encrypt(plainText)));
        }

        [Test]
        public void EncryptingAndDecryptingAStringReturnsSameValue()
        {

            string plainText = "StringToEncrypt";
            string encryptedText = EncodeDecodeHelper.Encrypt(plainText);
            string decryptedText = EncodeDecodeHelper.Decrypt(encryptedText);
            Assert.That(plainText, Is.EqualTo(decryptedText));
        }
        [Test]
        public void DecryptingMultipleTimesAStringReturnsSameValue()
        {
            string encryptedText = EncodeDecodeHelper.Encrypt("StringToEncrypt");
            string decryptedText = EncodeDecodeHelper.Decrypt(encryptedText);
            string decryptedTextB = EncodeDecodeHelper.Decrypt(encryptedText);
            Assert.That(decryptedText, Is.EqualTo(decryptedTextB));
        }

        [Test]
        public void CanNotInitializeHelperMultipleTimes()
        {
            Assert.Throws<InvalidOperationException>(() => EncodeDecodeHelper.Init("NewKey"));
        }
    }
}
