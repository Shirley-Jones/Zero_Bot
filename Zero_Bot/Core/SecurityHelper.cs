using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

public class SecurityHelper
{
    [Obfuscation(Feature = "ultra", Exclude = false)]
    public static string ClientEncrypt(string json, string publicKeyXml, out byte[] aesKey, out byte[] aesIv)
    {
        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.GenerateKey();
            aes.GenerateIV();
            aesKey = aes.Key;
            aesIv = aes.IV;

            string encryptedJson = AesEncrypt(json, aesKey, aesIv);

            byte[] keyIvPair = new byte[48];
            Buffer.BlockCopy(aesKey, 0, keyIvPair, 0, 32);
            Buffer.BlockCopy(aesIv, 0, keyIvPair, 32, 16);

            byte[] encryptedKeyIv;
            using (RSA rsa = RSA.Create())
            {
                rsa.FromXmlString(publicKeyXml);
                encryptedKeyIv = rsa.Encrypt(keyIvPair, RSAEncryptionPadding.Pkcs1);
            }

            return Convert.ToBase64String(encryptedKeyIv) + "|" + encryptedJson;
        }
    }

    [Obfuscation(Feature = "ultra", Exclude = false)]
    public static string ClientDecryptResponse(string encryptedJson, byte[] aesKey, byte[] aesIv)
    {
        return AesDecrypt(encryptedJson, aesKey, aesIv);
    }

    [Obfuscation(Feature = "ultra", Exclude = false)]
    private static string AesEncrypt(string plainText, byte[] key, byte[] iv)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            ICryptoTransform encryptor = aes.CreateEncryptor();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

            // 直接字节级加密，没有流嵌套
            using (MemoryStream ms = new MemoryStream())
            {
                using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    cs.Write(plainBytes, 0, plainBytes.Length);
                    cs.FlushFinalBlock();  // 显式刷入，避免 Dispose 时 VM 上下文出错
                }
                return Convert.ToBase64String(ms.ToArray());
            }
        }
    }

    [Obfuscation(Feature = "ultra", Exclude = false)]
    private static string AesDecrypt(string cipherText, byte[] key, byte[] iv)
    {
        byte[] cipherBytes = Convert.FromBase64String(cipherText);
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            ICryptoTransform decryptor = aes.CreateDecryptor();

            using (MemoryStream ms = new MemoryStream(cipherBytes))
            using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
            using (MemoryStream outMs = new MemoryStream())
            {
                byte[] buf = new byte[1024];
                int read;
                while ((read = cs.Read(buf, 0, buf.Length)) > 0)
                {
                    outMs.Write(buf, 0, read);
                }
                return Encoding.UTF8.GetString(outMs.ToArray());
            }
        }
    }

    // Server 端方法（如果你的服务端也用这个 dll，保持不变的逻辑）
    [Obfuscation(Feature = "ultra", Exclude = false)]
    public static string ServerDecrypt(string receivedData, string privateKeyXml, out byte[] aesKey, out byte[] aesIv)
    {
        string[] parts = receivedData.Split('|');
        if (parts.Length != 2) throw new Exception("非法的数据报文格式");

        byte[] encryptedKeyIv = Convert.FromBase64String(parts[0]);
        string encryptedJson = parts[1];

        byte[] keyIvPair;
        using (RSA rsa = RSA.Create())
        {
            rsa.FromXmlString(privateKeyXml);
            keyIvPair = rsa.Decrypt(encryptedKeyIv, RSAEncryptionPadding.Pkcs1);
        }

        aesKey = new byte[32];
        aesIv = new byte[16];
        Buffer.BlockCopy(keyIvPair, 0, aesKey, 0, 32);
        Buffer.BlockCopy(keyIvPair, 32, aesIv, 0, 16);

        return AesDecrypt(encryptedJson, aesKey, aesIv);
    }

    [Obfuscation(Feature = "ultra", Exclude = false)]
    public static string ServerEncryptResponse(string json, byte[] aesKey, byte[] aesIv)
    {
        return AesEncrypt(json, aesKey, aesIv);
    }
}