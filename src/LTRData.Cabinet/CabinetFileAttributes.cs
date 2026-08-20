namespace LTRData.Cabinet;

/// <summary>Attributes stored in a CFFILE record.</summary>
[Flags]
public enum CabinetFileAttributes : ushort
{
    ReadOnly = 0x0001,
    Hidden = 0x0002,
    System = 0x0004,
    Archive = 0x0020,
    Execute = 0x0040,
    NameIsUtf8 = 0x0080,
}
