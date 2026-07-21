using System;
using Windows.Security.Credentials;

namespace Starward.Helpers;

/// <summary>
/// Windows PasswordVault 读写封装，用于安全存放本机密钥（如 GitHub PAT）。
/// 不落盘明文、不写数据库；日志调用方须保证不输出 Password。
/// </summary>
public static class WindowsPasswordVaultStore
{

    /// <summary>
    /// 保存或覆盖凭据。
    /// </summary>
    /// <param name="resource">资源名（应用内约定唯一）。</param>
    /// <param name="userName">用户名（可与资源搭配定位）。</param>
    /// <param name="password">密钥明文（仅传入本方法，勿记录日志）。</param>
    /// <exception cref="ArgumentException">参数为空。</exception>
    public static void Save(string resource, string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var vault = new PasswordVault();
        // 先删再加，避免重复项
        try
        {
            PasswordCredential existing = vault.Retrieve(resource, userName);
            vault.Remove(existing);
        }
        catch (Exception)
        {
            // 不存在时 Retrieve 会抛，忽略
        }

        vault.Add(new PasswordCredential(resource, userName, password));
    }


    /// <summary>
    /// 读取已保存的密钥。
    /// </summary>
    /// <param name="resource">资源名。</param>
    /// <param name="userName">用户名。</param>
    /// <returns>密钥；不存在或失败时为 null。</returns>
    public static string? TryGetPassword(string resource, string userName)
    {
        if (string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        try
        {
            var vault = new PasswordVault();
            PasswordCredential credential = vault.Retrieve(resource, userName);
            credential.RetrievePassword();
            return string.IsNullOrWhiteSpace(credential.Password) ? null : credential.Password;
        }
        catch (Exception)
        {
            return null;
        }
    }


    /// <summary>
    /// 按资源名查找是否已有任意凭据（不要求事先知道 userName）。
    /// </summary>
    /// <param name="resource">资源名。</param>
    /// <param name="userName">输出：找到的第一条凭据用户名。</param>
    /// <param name="password">输出：密钥明文。</param>
    /// <returns>是否找到。</returns>
    public static bool TryFindByResource(string resource, out string? userName, out string? password)
    {
        userName = null;
        password = null;
        if (string.IsNullOrWhiteSpace(resource))
        {
            return false;
        }

        try
        {
            var vault = new PasswordVault();
            var list = vault.FindAllByResource(resource);
            if (list is null || list.Count == 0)
            {
                return false;
            }

            PasswordCredential first = list[0];
            first.RetrievePassword();
            if (string.IsNullOrWhiteSpace(first.Password))
            {
                return false;
            }

            userName = first.UserName;
            password = first.Password;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }


    /// <summary>
    /// 删除指定资源下全部凭据。
    /// </summary>
    /// <param name="resource">资源名。</param>
    public static void RemoveAll(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            return;
        }

        try
        {
            var vault = new PasswordVault();
            var list = vault.FindAllByResource(resource);
            if (list is null)
            {
                return;
            }

            foreach (PasswordCredential item in list)
            {
                try
                {
                    vault.Remove(item);
                }
                catch (Exception)
                {
                    // 单项删除失败不影响其余
                }
            }
        }
        catch (Exception)
        {
            // 资源不存在
        }
    }


    /// <summary>
    /// 是否已保存该资源的凭据。
    /// </summary>
    public static bool HasCredential(string resource)
    {
        return TryFindByResource(resource, out _, out _);
    }

}
