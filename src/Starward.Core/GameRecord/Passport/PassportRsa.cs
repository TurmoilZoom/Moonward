using System.Security.Cryptography;
using System.Text;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// 米游社 passport 登录用 RSA 加密（手机号 / 区号），算法与 JSEncrypt PKCS#1 v1.5 对齐。
/// </summary>
public static class PassportRsa
{

    /// <summary>
    /// 米哈游 passport 固定公钥（与网页端 / TeyvatGuide 一致）。
    /// </summary>
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDDvekdPMHN3AYhm/vktJT+YJr7
        cI5DcsNKqdsx5DZX0gDuWFuIjzdwButrIYPNmRJ1G8ybDIF7oDW2eEpm5sMbL9zs
        9ExXCdvqrn51qELbqj0XxtMTIpaCHFSI50PfPpTFV9Xt/hmyVwokoOXFlAEgCn+Q
        CgGs52bFoYMtyi+xEQIDAQAB
        -----END PUBLIC KEY-----
        """;


    private static readonly Lazy<RSA> Rsa = new(CreateRsa);


    /// <summary>
    /// 使用 passport 公钥对明文做 RSA 加密并返回 Base64 密文。
    /// </summary>
    /// <param name="plainText">待加密明文（如 <c>+86</c> 或手机号）；不可为 null。</param>
    /// <returns>Base64 编码的密文；明文为空时返回空字符串。</returns>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }
        byte[] data = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = Rsa.Value.Encrypt(data, RSAEncryptionPadding.Pkcs1);
        return Convert.ToBase64String(encrypted);
    }


    /// <summary>
    /// 从 PEM 公钥创建可复用的 <see cref="RSA"/> 实例。
    /// </summary>
    private static RSA CreateRsa()
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(PublicKeyPem);
        return rsa;
    }

}
