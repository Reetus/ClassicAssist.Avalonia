namespace ClassicAssist.UO.Data;

public struct LandTile
{
    public TileFlags Flags { get; set; }
    public int ID { get; set; }
    public string Name { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public sbyte Z { get; set; }

    /// <summary>
    ///     Void / no-draw land tiles that represent "nothing" and should not be treated as
    ///     real terrain (e.g. for surface / line of sight calculations).
    /// </summary>
    public bool Ignored => ID is 2 or 0x1DB or >= 0x1AE and <= 0x1B5;
}