using HyperCardSharp.Core.Containers;

namespace HyperCardSharp.Core.Tests;

public class ContainerPipelineErrorTests
{
    private static byte[] BuildStuffIt5Banner()
    {
        // StuffIt 5.x archives open with a human-readable ASCII banner, not the
        // classic "SIT!" magic. This is a minimal prefix matching the real format
        // (verified against samples/ContextualMenus.sit).
        var text = "StuffIt (c)1997-1998 Aladdin Systems, Inc., http://www.aladdinsys.com";
        var bytes = new byte[128];
        System.Text.Encoding.ASCII.GetBytes(text).CopyTo(bytes, 0);
        return bytes;
    }

    [Fact]
    public void StuffItExtractor_IsStuffIt5_DetectsAladdinBanner()
    {
        var data = BuildStuffIt5Banner();
        Assert.True(StuffItExtractor.IsStuffIt5(data));
    }

    [Fact]
    public void StuffItExtractor_IsStuffIt5_RejectsClassicSitBang()
    {
        var data = new byte[128];
        data[0] = (byte)'S'; data[1] = (byte)'I'; data[2] = (byte)'T'; data[3] = (byte)'!';
        Assert.False(StuffItExtractor.IsStuffIt5(data));
    }

    [Fact]
    public void UnwrapMultiple_Sit5Archive_LogsSpecificUnsupportedMessage()
    {
        var data = BuildStuffIt5Banner();
        var logLines = new List<string>();

        var stacks = ContainerPipeline.UnwrapMultiple(data, msg => logLines.Add(msg));

        Assert.Empty(stacks);
        Assert.Contains(logLines, l => l.Contains("StuffIt 5") && l.Contains("not yet supported"));
    }

    [Fact]
    public void UnwrapEntries_Sit5Archive_ReturnsNoEntriesWithoutThrowing()
    {
        var data = BuildStuffIt5Banner();

        var entries = ContainerPipeline.UnwrapEntries(data);

        Assert.Empty(entries);
    }
}
