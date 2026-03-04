namespace cunes.Cartridge;

internal interface IMapper
{
    byte MapperId { get; }

    MirroringMode Mirroring { get; }

    void Reset();

    bool CpuRead(ushort address, out int mappedAddress, out bool isPrgRam);

    bool CpuWrite(ushort address, byte data, out int mappedAddress, out bool isPrgRam);

    bool PpuRead(ushort address, out int mappedAddress);

    bool PpuWrite(ushort address, out int mappedAddress);

    void NotifyPpuAddress(ushort address, ulong ppuCycle)
    {
        _ = address;
        _ = ppuCycle;
    }

    bool ConsumeIrq()
    {
        return false;
    }

    void NotifyPpuScanlineIrqClock()
    {
    }
}
