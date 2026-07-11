using System.Buffers.Binary;
using HyperCardSharp.Core.Containers;

namespace HyperCardSharp.Core.Tests;

public class HfsReaderTests
{
    /// <summary>
    /// Test that HfsReader.IsHfs() correctly identifies HFS volumes via the
    /// primary signature check (0x4244 at MDB offset 0), independent of
    /// the creation-date heuristic fallback.
    /// </summary>
    [Fact]
    public void IsHfs_RecognizesTrueHfsSignature()
    {
        // Build a minimal disk image with a valid HFS MDB signature
        // but with an implausible timestamp (to bypass the heuristic).
        var disk = new byte[2048];  // at least MdbOffset + 10
        const int MdbOffset = 1024;

        // Write the correct HFS MDB signature (0x4244, ASCII "BD") at MDB+0
        BinaryPrimitives.WriteUInt16BigEndian(disk.AsSpan(MdbOffset, 2), 0x4244);

        // Write an implausible timestamp at MDB+2 so the fallback heuristic fails
        // (this forces the test to verify the primary signature check works alone)
        BinaryPrimitives.WriteUInt32BigEndian(disk.AsSpan(MdbOffset + 2, 4), 0x00000001);

        var reader = new HfsReader(disk);
        Assert.True(reader.IsHfs(), "HfsReader should recognize the correct 0x4244 signature even with implausible timestamp");
    }

    /// <summary>
    /// Test that the timestamp-plausibility fallback still works for
    /// non-standard disk-imaging tools that replace the signature word.
    /// </summary>
    [Fact]
    public void IsHfs_FallsBackToTimestampHeuristic()
    {
        // Build a minimal disk image with a non-standard signature but plausible timestamp
        var disk = new byte[2048];
        const int MdbOffset = 1024;

        // Write a wrong/non-standard signature at MDB+0
        BinaryPrimitives.WriteUInt16BigEndian(disk.AsSpan(MdbOffset, 2), 0xFFFF);

        // Write a plausible Mac timestamp (e.g. year 2000) at MDB+2
        // Mac time epoch is 1904-01-01; 2000-01-01 is roughly 0xD2C79BF0
        BinaryPrimitives.WriteUInt32BigEndian(disk.AsSpan(MdbOffset + 2, 4), 0xD2C79BF0);

        var reader = new HfsReader(disk);
        Assert.True(reader.IsHfs(), "HfsReader should fall back to timestamp heuristic for non-standard signature");
    }

    /// <summary>
    /// Test that IsHfs() rejects data that has neither a valid signature nor plausible timestamp.
    /// </summary>
    [Fact]
    public void IsHfs_RejectsInvalidData()
    {
        var disk = new byte[2048];
        const int MdbOffset = 1024;

        // Write wrong signature and implausible timestamp
        BinaryPrimitives.WriteUInt16BigEndian(disk.AsSpan(MdbOffset, 2), 0xFFFF);
        BinaryPrimitives.WriteUInt32BigEndian(disk.AsSpan(MdbOffset + 2, 4), 0x00000001);

        var reader = new HfsReader(disk);
        Assert.False(reader.IsHfs(), "HfsReader should reject data with neither valid signature nor plausible timestamp");
    }
}
