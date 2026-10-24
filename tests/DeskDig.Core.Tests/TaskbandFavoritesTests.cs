namespace DeskDig.Core.Tests;

public class TaskbandFavoritesTests
{
    private static byte[] Entry(params byte[] pidl)
    {
        var size = BitConverter.GetBytes(pidl.Length);
        return [0x00, size[0], size[1], size[2], size[3], .. pidl];
    }

    [Fact]
    public void Splits_entries_in_order_and_keeps_pidl_bytes()
    {
        byte[] first = [0x14, 0x00, 0x1f, 0x80, 0xAA, 0x00, 0x00];
        byte[] second = [0x04, 0x00, 0xBB, 0xCC, 0x00, 0x00];
        var blob = Entry(first).Concat(Entry(second)).ToArray();

        var pidls = TaskbandFavorites.Split(blob);

        Assert.Equal(2, pidls.Count);
        Assert.Equal(first, pidls[0]);
        Assert.Equal(second, pidls[1]);
    }

    [Fact]
    public void Stops_at_malformed_entry_and_returns_what_was_read()
    {
        byte[] good = [0x02, 0x00, 0x00, 0x00];
        byte[] notTerminated = [0x02, 0x00, 0x01];
        var blob = Entry(good).Concat(Entry(notTerminated)).Concat(Entry(good)).ToArray();

        var pidls = TaskbandFavorites.Split(blob);

        Assert.Single(pidls);
    }

    [Fact]
    public void Empty_or_truncated_blob_yields_nothing()
    {
        Assert.Empty(TaskbandFavorites.Split([]));
        Assert.Empty(TaskbandFavorites.Split([0x00, 0x10, 0x00, 0x00, 0x00, 0x01]));
    }
}
