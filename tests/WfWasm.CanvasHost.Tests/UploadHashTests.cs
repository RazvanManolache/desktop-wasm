using WfWasm.CanvasHost;
public sealed class UploadHashTests
{
    [Theory]
    [InlineData(0, 14695981039346656037UL)]
    [InlineData(1, 12638154215206795666UL)]
    [InlineData(2, 591717608750762504UL)]
    [InlineData(3, 16349476326249480699UL)]
    [InlineData(4, 4659464237584460690UL)]
    [InlineData(5, 13553995587652976791UL)]
    [InlineData(17, 7394589059512377486UL)]
    [InlineData(257, 1003458611241654226UL)]
    public void Upload_hash_keeps_existing_word_and_tail_contract(int length, ulong expected)
    {
        byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 71 + 3)).ToArray();
        Assert.Equal(expected, WinFormsAppHost.HashBytes(bytes));
    }
}
