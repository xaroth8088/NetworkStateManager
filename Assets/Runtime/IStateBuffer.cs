namespace NSM
{
    public interface IStateBuffer
    {
        StateFrameDTO this[int i] { get; set; }

        bool TryGetValue(int key, out StateFrameDTO value);
    }
}