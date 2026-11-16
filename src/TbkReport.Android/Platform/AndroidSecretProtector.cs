using System;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using TbkReport.Services;

namespace TbkReport.Mobile;

/// <summary>
/// Encrypts the mail app password with an AES key kept in the Android Keystore. The key never leaves the
/// device and is removed together with the application.
/// </summary>
public sealed class AndroidSecretProtector : ISecretProtector
{
    private const string KeyStoreName = "AndroidKeyStore";
    private const string KeyAlias = "tbk-report-mail";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int IvLength = 12;

    public byte[] Protect(byte[] data)
    {
        using var cipher = Cipher.GetInstance(Transformation) ?? throw new InvalidOperationException("Шифрование недоступно.");
        cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
        var iv = cipher.GetIV() ?? throw new InvalidOperationException("Шифрование недоступно.");
        var encrypted = cipher.DoFinal(data) ?? Array.Empty<byte>();
        var result = new byte[iv.Length + encrypted.Length];
        iv.CopyTo(result, 0);
        encrypted.CopyTo(result, iv.Length);
        return result;
    }

    public byte[] Unprotect(byte[] data)
    {
        if (data.Length <= IvLength) throw new InvalidOperationException("Сохранённый пароль повреждён.");
        using var cipher = Cipher.GetInstance(Transformation) ?? throw new InvalidOperationException("Шифрование недоступно.");
        cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(128, data[..IvLength]));
        return cipher.DoFinal(data[IvLength..]) ?? Array.Empty<byte>();
    }

    private static IKey GetOrCreateKey()
    {
        var store = KeyStore.GetInstance(KeyStoreName) ?? throw new InvalidOperationException("Android Keystore недоступен.");
        store.Load(null, null);
        if (store.GetKey(KeyAlias, null) is { } existing) return existing;
        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreName)
            ?? throw new InvalidOperationException("Android Keystore недоступен.");
        generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build());
        return generator.GenerateKey() ?? throw new InvalidOperationException("Не удалось создать ключ шифрования.");
    }
}
