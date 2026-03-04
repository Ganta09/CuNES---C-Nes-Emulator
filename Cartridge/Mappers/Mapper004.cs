namespace cunes.Cartridge.Mappers;

internal sealed class Mapper004 : IMapper
{
    private const int A12LowCyclesThreshold = 8;
    private const int DebugWriteLogLimit = 300;
    private readonly int _prgBankCount8k;
    private readonly int _chrBankCount1k;
    private readonly byte[] _bankRegisters = new byte[8];
    private readonly bool _debugEnabled;
    private readonly bool _useScanlineIrqClock;

    private byte _bankSelect;
    private bool _prgRamEnabled;
    private bool _prgRamWriteProtect;
    private byte _irqLatch;
    private byte _irqCounter;
    private bool _irqEnabled;
    private bool _irqReloadRequested;
    private bool _irqPending;
    private bool _a12WasHigh;
    private int _a12LowCycles;
    private int _debugWriteLogs;
    private long _debugLastSummaryMs;
    private int _debugA12RisesSinceSummary;
    private int _debugA12RejectedSinceSummary;
    private int _debugIrqClocksSinceSummary;
    private int _debugIrqAssertsSinceSummary;
    private int _debugIrqConsumedSinceSummary;
    private int _debugWriteC000SinceSummary;
    private int _debugWriteC001SinceSummary;
    private int _debugWriteE000SinceSummary;
    private int _debugWriteE001SinceSummary;
    private ulong _lastPpuNotifyCycle;
    private bool _hasLastPpuNotifyCycle;

    public Mapper004(
        int prgRomBytes,
        int chrRomBytes,
        MirroringMode mirroring,
        bool debugEnabled = false,
        bool useScanlineIrqClock = true)
    {
        _prgBankCount8k = Math.Max(1, prgRomBytes / 0x2000);
        _chrBankCount1k = Math.Max(8, chrRomBytes / 0x400);
        _debugEnabled = debugEnabled;
        _useScanlineIrqClock = useScanlineIrqClock;
        Mirroring = mirroring;
        Reset();
    }

    public byte MapperId => 4;

    public MirroringMode Mirroring { get; private set; }

    public void Reset()
    {
        Array.Clear(_bankRegisters);
        _bankSelect = 0;
        _prgRamEnabled = true;
        _prgRamWriteProtect = false;
        _irqLatch = 0;
        _irqCounter = 0;
        _irqEnabled = false;
        _irqReloadRequested = false;
        _irqPending = false;
        _a12WasHigh = false;
        _a12LowCycles = A12LowCyclesThreshold;
        _debugWriteLogs = 0;
        _debugLastSummaryMs = Environment.TickCount64;
        _debugA12RisesSinceSummary = 0;
        _debugA12RejectedSinceSummary = 0;
        _debugIrqClocksSinceSummary = 0;
        _debugIrqAssertsSinceSummary = 0;
        _debugIrqConsumedSinceSummary = 0;
        _debugWriteC000SinceSummary = 0;
        _debugWriteC001SinceSummary = 0;
        _debugWriteE000SinceSummary = 0;
        _debugWriteE001SinceSummary = 0;
        _lastPpuNotifyCycle = 0;
        _hasLastPpuNotifyCycle = false;
        if (_debugEnabled)
        {
            Console.WriteLine("[MMC3] debug enabled");
            Console.WriteLine($"[MMC3] irq clock mode={(_useScanlineIrqClock ? "scanline-dot260" : "a12-rising")}");
        }
    }

    public bool CpuRead(ushort address, out int mappedAddress, out bool isPrgRam)
    {
        if (address is >= 0x6000 and <= 0x7FFF)
        {
            if (!_prgRamEnabled)
            {
                mappedAddress = -1;
                isPrgRam = false;
                return false;
            }

            mappedAddress = address - 0x6000;
            isPrgRam = true;
            return true;
        }

        if (address < 0x8000)
        {
            mappedAddress = -1;
            isPrgRam = false;
            return false;
        }

        var prgMode = (_bankSelect & 0x40) != 0;
        var lastBank = _prgBankCount8k - 1;
        var secondLastBank = Math.Max(0, _prgBankCount8k - 2);

        int selectedBank = address switch
        {
            <= 0x9FFF => prgMode ? secondLastBank : (_bankRegisters[6] & 0x3F),
            <= 0xBFFF => _bankRegisters[7] & 0x3F,
            <= 0xDFFF => prgMode ? (_bankRegisters[6] & 0x3F) : secondLastBank,
            _ => lastBank
        };

        selectedBank %= _prgBankCount8k;
        mappedAddress = selectedBank * 0x2000 + (address & 0x1FFF);
        isPrgRam = false;
        return true;
    }

    public bool CpuWrite(ushort address, byte data, out int mappedAddress, out bool isPrgRam)
    {
        if (address is >= 0x6000 and <= 0x7FFF)
        {
            if (!_prgRamEnabled || _prgRamWriteProtect)
            {
                mappedAddress = -1;
                isPrgRam = false;
                return true;
            }

            mappedAddress = address - 0x6000;
            isPrgRam = true;
            return true;
        }

        if (address < 0x8000)
        {
            mappedAddress = -1;
            isPrgRam = false;
            return false;
        }

        if (address <= 0x9FFF)
        {
            if ((address & 0x0001) == 0)
            {
                _bankSelect = data;
                DebugWrite(address, data, $"bankSelect=0x{_bankSelect:X2} prgMode={((_bankSelect & 0x40) != 0 ? 1 : 0)} chrMode={((_bankSelect & 0x80) != 0 ? 1 : 0)}");
            }
            else
            {
                _bankRegisters[_bankSelect & 0x07] = data;
                DebugWrite(address, data, $"bankReg[{_bankSelect & 0x07}]=0x{data:X2}");
            }
        }
        else if (address <= 0xBFFF)
        {
            if ((address & 0x0001) == 0)
            {
                Mirroring = (data & 0x01) == 0 ? MirroringMode.Vertical : MirroringMode.Horizontal;
                DebugWrite(address, data, $"mirroring={Mirroring}");
            }
            else
            {
                _prgRamEnabled = (data & 0x80) != 0;
                _prgRamWriteProtect = (data & 0x40) != 0;
                DebugWrite(address, data, $"prgRamEnabled={_prgRamEnabled} writeProtect={_prgRamWriteProtect}");
            }
        }
        else if (address <= 0xDFFF)
        {
            if ((address & 0x0001) == 0)
            {
                _irqLatch = data;
                _debugWriteC000SinceSummary++;
                DebugWrite(address, data, $"irqLatch={_irqLatch}");
            }
            else
            {
                _irqReloadRequested = true;
                _debugWriteC001SinceSummary++;
                DebugWrite(address, data, "irqReloadRequested=true");
            }
        }
        else
        {
            if ((address & 0x0001) == 0)
            {
                _irqEnabled = false;
                _irqPending = false;
                _debugWriteE000SinceSummary++;
                DebugWrite(address, data, "irqEnabled=false irqPendingCleared=true");
            }
            else
            {
                _irqEnabled = true;
                _debugWriteE001SinceSummary++;
                DebugWrite(address, data, "irqEnabled=true");
            }
        }

        mappedAddress = -1;
        isPrgRam = false;
        return true;
    }

    public bool PpuRead(ushort address, out int mappedAddress)
    {
        if (address > 0x1FFF)
        {
            mappedAddress = -1;
            return false;
        }

        var chrMode = (_bankSelect & 0x80) != 0;
        int bank1k;

        if (!chrMode)
        {
            bank1k = address switch
            {
                <= 0x03FF => _bankRegisters[0] & 0xFE,
                <= 0x07FF => (_bankRegisters[0] & 0xFE) + 1,
                <= 0x0BFF => _bankRegisters[1] & 0xFE,
                <= 0x0FFF => (_bankRegisters[1] & 0xFE) + 1,
                <= 0x13FF => _bankRegisters[2],
                <= 0x17FF => _bankRegisters[3],
                <= 0x1BFF => _bankRegisters[4],
                _ => _bankRegisters[5]
            };
        }
        else
        {
            bank1k = address switch
            {
                <= 0x03FF => _bankRegisters[2],
                <= 0x07FF => _bankRegisters[3],
                <= 0x0BFF => _bankRegisters[4],
                <= 0x0FFF => _bankRegisters[5],
                <= 0x13FF => _bankRegisters[0] & 0xFE,
                <= 0x17FF => (_bankRegisters[0] & 0xFE) + 1,
                <= 0x1BFF => _bankRegisters[1] & 0xFE,
                _ => (_bankRegisters[1] & 0xFE) + 1
            };
        }

        bank1k %= _chrBankCount1k;
        mappedAddress = bank1k * 0x400 + (address & 0x03FF);
        return true;
    }

    public bool PpuWrite(ushort address, out int mappedAddress)
    {
        return PpuRead(address, out mappedAddress);
    }

    public void NotifyPpuAddress(ushort address, ulong ppuCycle)
    {
        if (_useScanlineIrqClock)
        {
            return;
        }

        var elapsedCycles = 0UL;
        if (_hasLastPpuNotifyCycle && ppuCycle > _lastPpuNotifyCycle)
        {
            elapsedCycles = ppuCycle - _lastPpuNotifyCycle;
        }

        _lastPpuNotifyCycle = ppuCycle;
        _hasLastPpuNotifyCycle = true;

        var a12High = (address & 0x1000) != 0;
        if (!a12High)
        {
            _a12WasHigh = false;
            if (elapsedCycles > 0 && _a12LowCycles < 1024)
            {
                var increment = elapsedCycles > 1024 ? 1024 : (int)elapsedCycles;
                _a12LowCycles = Math.Min(1024, _a12LowCycles + increment);
            }

            return;
        }

        if (!_a12WasHigh && _a12LowCycles >= A12LowCyclesThreshold)
        {
            _debugA12RisesSinceSummary++;
            ClockIrqCounter();
        }
        else if (!_a12WasHigh)
        {
            _debugA12RejectedSinceSummary++;
        }

        _a12WasHigh = true;
        _a12LowCycles = 0;
        MaybeLogSummary();
    }

    public void NotifyPpuScanlineIrqClock()
    {
        if (!_useScanlineIrqClock)
        {
            return;
        }

        ClockIrqCounter();
        MaybeLogSummary();
    }

    public bool ConsumeIrq()
    {
        if (!_irqPending)
        {
            return false;
        }

        _irqPending = false;
        _debugIrqConsumedSinceSummary++;
        MaybeLogSummary();
        return true;
    }

    private void ClockIrqCounter()
    {
        _debugIrqClocksSinceSummary++;
        var reachedZeroByDecrement = false;
        if (_irqCounter == 0 || _irqReloadRequested)
        {
            _irqCounter = _irqLatch;
            _irqReloadRequested = false;
        }
        else
        {
            _irqCounter--;
            reachedZeroByDecrement = _irqCounter == 0;
        }

        // MMC3 strict behavior: assert IRQ only on non-zero -> zero transition by decrement.
        if (reachedZeroByDecrement && _irqEnabled)
        {
            _irqPending = true;
            _debugIrqAssertsSinceSummary++;
        }
    }

    private void DebugWrite(ushort address, byte data, string details)
    {
        if (!_debugEnabled)
        {
            return;
        }

        if (_debugWriteLogs < DebugWriteLogLimit)
        {
            Console.WriteLine($"[MMC3] W ${address:X4}=${data:X2} | {details} | irq(latch={_irqLatch},ctr={_irqCounter},en={_irqEnabled},rel={_irqReloadRequested},pend={_irqPending})");
            _debugWriteLogs++;
            if (_debugWriteLogs == DebugWriteLogLimit)
            {
                Console.WriteLine($"[MMC3] write log limit reached ({DebugWriteLogLimit}), switching to summary mode");
            }
        }

        MaybeLogSummary();
    }

    private void MaybeLogSummary()
    {
        if (!_debugEnabled)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _debugLastSummaryMs < 1000)
        {
            return;
        }

        Console.WriteLine(
            $"[MMC3] summary a12Rise={_debugA12RisesSinceSummary} a12Rejected={_debugA12RejectedSinceSummary} " +
            $"wC000={_debugWriteC000SinceSummary} wC001={_debugWriteC001SinceSummary} wE000={_debugWriteE000SinceSummary} wE001={_debugWriteE001SinceSummary} " +
            $"irqClocks={_debugIrqClocksSinceSummary} irqAssert={_debugIrqAssertsSinceSummary} irqConsumed={_debugIrqConsumedSinceSummary} " +
            $"state(latch={_irqLatch},ctr={_irqCounter},en={_irqEnabled},rel={_irqReloadRequested},pend={_irqPending})");

        _debugLastSummaryMs = now;
        _debugA12RisesSinceSummary = 0;
        _debugA12RejectedSinceSummary = 0;
        _debugWriteC000SinceSummary = 0;
        _debugWriteC001SinceSummary = 0;
        _debugWriteE000SinceSummary = 0;
        _debugWriteE001SinceSummary = 0;
        _debugIrqClocksSinceSummary = 0;
        _debugIrqAssertsSinceSummary = 0;
        _debugIrqConsumedSinceSummary = 0;
    }
}
