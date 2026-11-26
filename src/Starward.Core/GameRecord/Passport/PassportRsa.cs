using System.Security.Cryptography;
using System.Text;

namespace Starward.Core.GameRecord.Passport;

/// <summary>
/// passport 登录用 RSA 加密（手机号 / 区号 / 账号 / 密码），算法与 JSEncrypt PKCS#1 v1.5 对齐。
/// 国服与国际服各用一把公钥。
/// </summary>
public static class PassportRsa
{

    /// <summary>
    /// 米哈游（国服）passport 固定公钥（与网页端 / TeyvatGuide 一致）。
    /// </summary>
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDDvekdPMHN3AYhm/vktJT+YJr7
        cI5DcsNKqdsx5DZX0gDuWFuIjzdwButrIYPNmRJ1G8ybDIF7oDW2eEpm5sMbL9zs
        9ExXCdvqrn51qELbqj0XxtMTIpaCHFSI50PfPpTFV9Xt/hmyVwokoOXFlAEgCn+Q
        CgGs52bFoYMtyi+xEQIDAQAB
        -----END PUBLIC KEY-----
        """;


    /// <summary>
    /// HoYoverse（国际服）passport 生产环境公钥，取自 HYP 国际服前端账号 SDK 的 production 配置（ENCRYPT_KEY）。
    /// </summary>
    private const string GlobalPublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA4PMS2JVMwBsOIrYWRluY
        wEiFZL7Aphtm9z5Eu/anzJ09nB00uhW+ScrDWFECPwpQto/GlOJYCUwVM/raQpAj
        /xvcjK5tNVzzK94mhk+j9RiQ+aWHaTXmOgurhxSp3YbwlRDvOgcq5yPiTz0+kSeK
        ZJcGeJ95bvJ+hJ/UMP0Zx2qB5PElZmiKvfiNqVUk8A8oxLJdBB5eCpqWV6CUqDKQ
        KSQP4sM0mZvQ1Sr4UcACVcYgYnCbTZMWhJTWkrNXqI8TMomekgny3y+d6NX/cFa6
        6jozFIF4HCX5aW8bp8C8vq2tFvFbleQ/Q3CU56EWWKMrOcpmFtRmC18s9biZBVR/
        8QIDAQAB
        -----END PUBLIC KEY-----
        """;


    private static readonly Lazy<RSA> Rsa = new(() => CreateRsa(PublicKeyPem));

    private static readonly Lazy<RSA> GlobalRsa = new(() => CreateRsa(GlobalPublicKeyPem));


    /// <summary>
    /// 使用国服 passport 公钥对明文做 RSA 加密并返回 Base64 密文。
    /// </summary>
    /// <param name="plainText">待加密明文（如 <c>+86</c> 或手机号）；不可为 null。</param>
    /// <returns>Base64 编码的密文；明文为空时返回空字符串。</returns>
    public static string Encrypt(string plainText)
    {
        return Encrypt(Rsa.Value, plainText);
    }


    /// <summary>
    /// 使用国际服 passport 公钥对明文做 RSA 加密并返回 Base64 密文。
    /// </summary>
    /// <param name="plainText">待加密明文（账号或密码）；不可为 null。</param>
    /// <returns>Base64 编码的密文；明文为空时返回空字符串。</returns>
    public static string EncryptGlobal(string plainText)
    {
        return Encrypt(GlobalRsa.Value, plainText);
    }


    /// <summary>
    /// 用指定公钥做 PKCS#1 v1.5 加密。
    /// </summary>
    /// <param name="rsa">公钥实例。</param>
    /// <param name="plainText">待加密明文。</param>
    /// <returns>Base64 编码的密文；明文为空时返回空字符串。</returns>
    private static string Encrypt(RSA rsa, string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }
        byte[] data = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = rsa.Encrypt(data, RSAEncryptionPadding.Pkcs1);
        return Convert.ToBase64String(encrypted);
    }


    /// <summary>
    /// 从 PEM 公钥创建可复用的 <see cref="RSA"/> 实例。
    /// </summary>
    /// <param name="pem">PEM 格式公钥。</param>
    /// <returns>已导入公钥的实例。</returns>
    private static RSA CreateRsa(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

}
