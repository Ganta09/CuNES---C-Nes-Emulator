# CuNES v0.1.1

## Highlights

- AccuracyCoin progress improved to **85 / 136**.
- APU timing and behavior fixes:
  - Length Counter / Length Table stability improvements.
  - Frame Counter IRQ timing adjustments.
- PPU behavior fixes:
  - PPU reset flag handling after power-on.
  - PPU open bus decay behavior.
  - Palette RAM 6-bit behavior with upper bits from open bus.
- CPU side updates and unofficial opcode work continued.
- Window title now shows current emulator FPS.
- Audio latency reduced (sound lag fix).

## Notes

- AccuracyCoin is used as a primary validation suite for timing and edge cases.
- Thanks to **100thCoin** for providing and maintaining AccuracyCoin:
  https://github.com/100thCoin/AccuracyCoin

## Known Limitations

- Some PPU behavior tests still fail and are under active investigation.
- Overall hardware-accuracy work is still ongoing.
