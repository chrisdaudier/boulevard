using System.Text;

namespace Boulevard.Protocol.Fix.Tests;

public class FixMessageReaderTests
{
    [Fact]
    public void EnumeratesTagValuePairsInOrder()
    {
        byte[] raw = Encoding.ASCII.GetBytes("1=A\u00012=BB\u00013=\u0001");

        List<(int Tag, string Value)> fields = [];
        foreach (FixField field in new FixMessageReader(raw))
        {
            fields.Add((field.Tag, Encoding.ASCII.GetString(field.Value)));
        }

        Assert.Equal([(1, "A"), (2, "BB"), (3, "")], fields);
    }

    [Fact]
    public void StopsCleanlyOnEmptyInput()
    {
        FixMessageReader.Enumerator enumerator = new FixMessageReader(ReadOnlySpan<byte>.Empty).GetEnumerator();

        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void StopsWithoutThrowingWhenNoEqualsSign()
    {
        byte[] raw = Encoding.ASCII.GetBytes("garbage\u0001");
        FixMessageReader.Enumerator enumerator = new FixMessageReader(raw).GetEnumerator();

        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void StopsWithoutThrowingWhenTagIsNonNumeric()
    {
        byte[] raw = Encoding.ASCII.GetBytes("AB=1\u0001");
        FixMessageReader.Enumerator enumerator = new FixMessageReader(raw).GetEnumerator();

        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void StopsWithoutThrowingWhenFinalFieldUnterminated()
    {
        byte[] raw = Encoding.ASCII.GetBytes("1=A\u00012=B"); // no trailing SOH
        FixMessageReader.Enumerator enumerator = new FixMessageReader(raw).GetEnumerator();

        Assert.True(enumerator.MoveNext()); // "1=A" is fine
        Assert.False(enumerator.MoveNext()); // "2=B" has no terminating SOH
    }

    [Fact]
    public void ConsumedBytesTracksOffsetAfterEachField()
    {
        byte[] raw = Encoding.ASCII.GetBytes("1=A\u00012=BB\u0001");
        FixMessageReader.Enumerator enumerator = new FixMessageReader(raw).GetEnumerator();

        Assert.True(enumerator.MoveNext());
        Assert.Equal(4, enumerator.ConsumedBytes); // "1=A\u0001"

        Assert.True(enumerator.MoveNext());
        Assert.Equal(9, enumerator.ConsumedBytes); // "1=A\u00012=BB\u0001"
    }
}
