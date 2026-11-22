using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Starward.Helpers;

internal static class ClipboardHelper
{

    public static void SetText(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var data = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            data.SetText(value);
            Clipboard.SetContent(data);
        }
    }


    /// <summary>
    /// 复制文本并立即交给系统剪贴板保管（<see cref="Clipboard.Flush"/>），供马上就要退出的进程使用：
    /// 不 Flush 时剪贴板里只是延迟渲染的数据包，进程一退出内容就没了。
    /// </summary>
    /// <param name="value">要复制的文本。</param>
    /// <returns>成功返回 <see langword="true"/>；空文本或剪贴板一直被其他程序占用时返回 <see langword="false"/>。</returns>
    public static async Task<bool> SetTextAndFlushAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                SetText(value);
                Clipboard.Flush();
                return true;
            }
            catch (Exception) when (attempt < 5)
            {
                // 剪贴板被别的程序打开着（CLIPBRD_E_CANT_OPEN）时稍等再试
                await Task.Delay(100);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }


    public static void SetBitmap(IRandomAccessStream stream)
    {
        var value = RandomAccessStreamReference.CreateFromStream(stream);
        var data = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        data.SetBitmap(value);
        Clipboard.SetContent(data);
    }


    public static void SetBitmap(IStorageFile file)
    {
        var value = RandomAccessStreamReference.CreateFromFile(file);
        var data = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        data.SetBitmap(value);
        Clipboard.SetContent(data);
    }


    public static void SetBitmap(Uri uri)
    {
        var value = RandomAccessStreamReference.CreateFromUri(uri);
        var data = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        data.SetBitmap(value);
        Clipboard.SetContent(data);
    }



    public static void SetStorageItems(DataPackageOperation operation, params IStorageItem[] items)
    {
        var data = new DataPackage
        {
            RequestedOperation = operation,
        };
        data.SetStorageItems(items);
        Clipboard.SetContent(data);
    }



    public static async Task<string?> GetTextAsync()
    {
        var data = Clipboard.GetContent();
        if (data.Contains(StandardDataFormats.Text))
        {
            return await data.GetTextAsync();
        }
        else
        {
            return null;
        }
    }


}
