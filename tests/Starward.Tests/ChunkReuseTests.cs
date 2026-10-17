using Starward.RPC.GameInstall;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// Chunk 模式更新：新版本文件的块能否从本地旧文件复用（<see cref="GameInstallFile.FromSophonChunkFile"/>）。
/// 复用的块不联网下载，直接从旧文件切出；判断错了会下错文件或白白重下。
/// </summary>
public class ChunkReuseTests
{

    private const string InstallPath = @"C:\Games\Test";

    private const string UrlPrefix = "https://example.com/chunks/";


    private static SophonChunk Chunk(string id, string md5, long size, long offset = 0)
    {
        return new SophonChunk
        {
            Id = id,
            UncompressedMd5 = md5,
            UncompressedSize = size,
            CompressedMd5 = $"compressed-{md5}",
            CompressedSize = size / 2,
            Offset = offset,
        };
    }


    private static SophonChunkFile File(string path, params SophonChunk[] chunks)
    {
        var file = new SophonChunkFile { File = path, Size = chunks.Sum(x => x.UncompressedSize), Md5 = $"md5-{path}" };
        file.Chunks.Add(chunks);
        return file;
    }



    [Fact]
    public void NoLocalFile_NothingReused()
    {
        var result = GameInstallFile.FromSophonChunkFile(File("data/a.blk", Chunk("c1", "A", 100)), null, InstallPath, UrlPrefix);

        Assert.Equal(@"C:\Games\Test\data\a.blk", result.FullPath);
        var chunk = Assert.Single(result.Chunks!);
        Assert.Equal("https://example.com/chunks/c1", chunk.Url);
        Assert.Null(chunk.OriginalFileName);
    }


    [Fact]
    public void SamePathChunkWithSameMd5AndSize_ReusedFromLocalFile()
    {
        var newFile = File("data/a.blk", Chunk("c1", "A", 100), Chunk("c2", "B", 200));
        var localFile = File("data/a.blk", Chunk("old", "B", 200, offset: 100));

        var chunks = GameInstallFile.FromSophonChunkFile(newFile, localFile, InstallPath, UrlPrefix).Chunks!;

        Assert.Null(chunks[0].OriginalFileName);
        Assert.Equal("data/a.blk", chunks[1].OriginalFileName);
        Assert.Equal(@"C:\Games\Test\data\a.blk", chunks[1].OriginalFileFullPath);
        Assert.Equal(100, chunks[1].OriginalFileOffset);
        Assert.Equal("B", chunks[1].OriginalFileMD5);
        Assert.Equal(localFile.Size, chunks[1].OriginalFileSize);
    }


    [Fact]
    public void SameMd5ButDifferentSize_NotReused()
    {
        var newFile = File("data/a.blk", Chunk("c1", "A", 100));
        var localFile = File("data/a.blk", Chunk("old", "A", 99));

        var chunk = Assert.Single(GameInstallFile.FromSophonChunkFile(newFile, localFile, InstallPath, UrlPrefix).Chunks!);
        Assert.Null(chunk.OriginalFileName);
    }


    [Fact]
    public void ChunkFoundOnlyInRemovedFile_ReusedFromRemovedFile()
    {
        // 崩坏3 的 asb 内容一变就换文件名：同路径找不到，块在新版本已移除的旧文件里
        var newFile = File("asb/new.asb", Chunk("c1", "C", 300));
        var removedFile = File("asb/old.asb", Chunk("old", "C", 300, offset: 50));
        var removed = new Dictionary<string, (SophonChunkFile File, SophonChunk Chunk)> { ["C"] = (removedFile, removedFile.Chunks[0]) };

        var chunk = Assert.Single(GameInstallFile.FromSophonChunkFile(newFile, null, InstallPath, UrlPrefix, removed).Chunks!);

        Assert.Equal("asb/old.asb", chunk.OriginalFileName);
        Assert.Equal(@"C:\Games\Test\asb\old.asb", chunk.OriginalFileFullPath);
        Assert.Equal(50, chunk.OriginalFileOffset);
    }


    [Fact]
    public void ChunkInBothLocalAndRemovedFile_PrefersLocalFile()
    {
        var newFile = File("data/a.blk", Chunk("c1", "D", 400));
        var localFile = File("data/a.blk", Chunk("local", "D", 400, offset: 10));
        var removedFile = File("data/gone.blk", Chunk("gone", "D", 400, offset: 20));
        var removed = new Dictionary<string, (SophonChunkFile File, SophonChunk Chunk)> { ["D"] = (removedFile, removedFile.Chunks[0]) };

        var chunk = Assert.Single(GameInstallFile.FromSophonChunkFile(newFile, localFile, InstallPath, UrlPrefix, removed).Chunks!);

        Assert.Equal("data/a.blk", chunk.OriginalFileName);
        Assert.Equal(10, chunk.OriginalFileOffset);
    }


    [Fact]
    public void DuplicateMd5InLocalFile_FirstOccurrenceUsed()
    {
        var newFile = File("data/a.blk", Chunk("c1", "E", 100));
        var localFile = File("data/a.blk", Chunk("first", "E", 100, offset: 0), Chunk("second", "E", 100, offset: 500));

        var chunk = Assert.Single(GameInstallFile.FromSophonChunkFile(newFile, localFile, InstallPath, UrlPrefix).Chunks!);
        Assert.Equal(0, chunk.OriginalFileOffset);
    }

}
