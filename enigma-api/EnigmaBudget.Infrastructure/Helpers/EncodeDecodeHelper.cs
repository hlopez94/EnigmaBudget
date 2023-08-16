using System.Text;

namespace EnigmaBudget.Infrastructure.Helpers
{
    public static class EncodeDecodeHelper
    {
        private static string _encriptionKey;

        public static void Init(string key)
        {
            if(string.IsNullOrEmpty(_encriptionKey))
                _encriptionKey = key;
            else throw new InvalidOperationException("Encription Key already initialized");
        }

        public static string Encrypt(long data)
        {
            Encoding unicode = Encoding.Unicode;

            return Convert.ToBase64String(Encrypt(unicode.GetBytes(_encriptionKey), unicode.GetBytes(data.ToString())));
        }
        public static string Encrypt(string data)
        {
            if(string.IsNullOrEmpty(data))
            {
                throw new ArgumentNullException(nameof(data), "Can't encrypt null value");
            }
            Encoding unicode = Encoding.Unicode;

            return Convert.ToBase64String(Encrypt(unicode.GetBytes(_encriptionKey), unicode.GetBytes(data)));
        }

        public static string Decrypt(string data)
        {
            if(string.IsNullOrEmpty(data))
            {
                throw new ArgumentNullException(nameof(data), "Can't encrypt null value");
            }
            Encoding unicode = Encoding.Unicode;

            return unicode.GetString(Encrypt(unicode.GetBytes(_encriptionKey), Convert.FromBase64String(data)));
        }

        public static long DecryptLong(string id)
        {
            return long.Parse(Decrypt(id));
        }
        private static byte[] Encrypt(byte[] key, byte[] data)
        {
            if(key == null || key.Length == 0)
            {
                throw new ArgumentNullException(nameof(key), "Can't encrypt null value");
            }
            if(data == null || data.Length == 0)
            {
                throw new ArgumentNullException(nameof(data), "Can't encrypt null value");
            }
            return EncryptOutput(key, data).ToArray();
        }

        private static byte[] EncryptInitalize(byte[] key)
        {
            byte[] s = Enumerable.Range(0, 256)
              .Select(i => (byte)i)
              .ToArray();

            for(int i = 0, j = 0; i < 256; i++)
            {
                j = (j + key[i % key.Length] + s[i]) & 255;

                Swap(s, i, j);
            }

            return s;
        }

        private static IEnumerable<byte> EncryptOutput(byte[] key, IEnumerable<byte> data)
        {
            byte[] s = EncryptInitalize(key);

            int i = 0;
            int j = 0;

            return data.Select((b) =>
            {
                i = (i + 1) & 255;
                j = (j + s[i]) & 255;

                Swap(s, i, j);

                return (byte)(b ^ s[(s[i] + s[j]) & 255]);
            });
        }

        private static void Swap(byte[] s, int i, int j)
        {
            byte c = s[i];

            s[i] = s[j];
            s[j] = c;
        }
    }
}
