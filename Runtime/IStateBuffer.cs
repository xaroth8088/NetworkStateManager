namespace NSM
{
    public interface IStateBuffer
    {
        StateFrameDTO this[int i] { get; set; }
    }
}