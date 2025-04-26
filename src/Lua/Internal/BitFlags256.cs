namespace Lua.Internal;

internal unsafe struct BitFlags256
{
    internal fixed long Data[4];

    public bool this[int index]
    {
        get => (Data[index >> 6] & (1L << (index & 63))) != 0;
        set
        {
            if (value)
            {
                Data[index >> 6] |= 1L << (index & 63);
            }
            else
            {
                Data[index >> 6] &= ~(1L << (index & 63));
            }
        }
    }

    public void Set(int index) => Data[index >> 6] |= 1L << (index & 63);
}