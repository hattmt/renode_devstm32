// Derived from 1.15.0 STM32H7_SPI.cs
//
// Fix typo in DMAReceive naming
// Add DMATransmit and transmitDMAEnabled
// Do not always call peripheral TransmissionFinished() at the end of a transfer
// Track "unhandled" Configuration2 fields to avoid Warnings and return state if read
//
// Original assignment:
//
// Copyright (c) 2010-2023 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Utilities;
using Antmicro.Renode.Utilities.Collections;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class STM32H7_SPI_Fixed : NullRegistrationPointPeripheralContainer<ISPIPeripheral>, IKnownSize, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral
    {
        public STM32H7_SPI_Fixed(Machine machine) : base(machine)
        {
            registers = new DoubleWordRegisterCollection(this);
            IRQ = new GPIO();
            DMAReceive = new GPIO();
            DMATransmit = new GPIO();

            transmitFifo = new Queue<uint>();
            receiveFifo = new Queue<uint>();

            DefineRegisters();
            Reset();
        }

        public override void Reset()
        {
            IRQ.Unset();
            DMAReceive.Unset();
            DMATransmit.Unset();
            iolockValue = false;
            transmittedPackets = 0;
            transmitFifo.Clear();
            receiveFifo.Clear();
            registers.Reset();
        }

        // We can't use AllowedTranslations because then WriteByte/WriteWord will trigger
        // an additional read (see ReadWriteExtensions:WriteByteUsingDword).
        // We can't have this happenning for the data register.
        public byte ReadByte(long offset)
        {
            return (byte)ReadDoubleWord(offset);
        }

        public void WriteByte(long offset, byte value)
        {
            WriteDoubleWord(offset, value);
        }

        public ushort ReadWord(long offset)
        {
            return (ushort)ReadDoubleWord(offset);
        }

        public void WriteWord(long offset, ushort value)
        {
            WriteDoubleWord(offset, value);
        }

        public uint ReadDoubleWord(long offset)
        {
            //return registers.Read(offset);
            uint value = registers.Read(offset);
            //this.Log(LogLevel.Debug, "ReadDoubleWord:  0x{0:X} value 0x{1:X}", offset, value);
            return value;
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            if(CanWriteToRegister((Registers)offset, value))
            {
                this.Log(LogLevel.Debug, "WriteDoubleWord: 0x{0:X} rval 0x{1:X}", offset, value);
                registers.Write(offset, value);
            }
        }

        public long Size => 0x400;

        public GPIO IRQ { get; }
        public GPIO DMAReceive { get; }
        public GPIO DMATransmit { get; }

        protected virtual bool IsWba { get; } = false;

        private void DefineRegisters()
        {
            Registers.Control1.Define(registers)
                .WithFlag(0, out peripheralEnabled, name: "SPE", changeCallback: (_, value) =>
                    {
                        this.Log(LogLevel.Debug, "Registers.Control1:SPE: value {0}", value);
                        if(value)
                        {
                            TryTransmitData();
                        }
                        else
                        {
                            this.Log(LogLevel.Debug, "Registers.Control1:SPI:false: calling ResetTransmissionState()");
                            ResetTransmissionState();
                            transmitFifo.Clear();
                            receiveFifo.Clear();
                            transmissionSize.Value = 0;
                        }
                    })
                .WithReservedBits(1, 7)
                .WithTaggedFlag("MASRX", 8)
                .WithFlag(9, out startTransmission, FieldMode.Read | FieldMode.Set, name: "CSTART", changeCallback: (_, value) =>
                    {
                        if(value)
                        {
                            endOfTransfer.Value = false;
                            TryTransmitData();
                        }
                    })
                .WithTaggedFlag("CSUSP", 10)
                .WithTaggedFlag("HDDIR", 11)
                .WithFlag(12, name: "SSI")
                .WithTaggedFlag("CRC33_17", 13)
                .WithTaggedFlag("RCRCINI", 14)
                .WithTaggedFlag("TCRCINI", 15)
                .WithFlag(16, name: "IOLOCK", valueProviderCallback: _ => iolockValue, changeCallback: (_, value) =>
                    {
                        if(value && !peripheralEnabled.Value)
                        {
                            this.Log(LogLevel.Warning, "Attempted to set IOLOCK while peripheral is enabled");
                            return;
                        }

                        iolockValue = value;
                    })
                .WithReservedBits(17, 15);

            Registers.Control2.Define(registers)
                .WithValueField(0, 16, out transmissionSize, name: "TSIZE")
                .If(IsWba)
                    .Then(r => r.WithReservedBits(16, 16))
                    .Else(r => r.WithTag("TSER", 16, 16));

            Registers.Configuration1.Define(registers)
                .WithValueField(0, 5, out packetSizeBits, name: "DSIZE")
                .WithValueField(5,4, name: "FTHLV")
                .If(IsWba)
                    .Then(r => r
                        .WithTaggedFlag("UDRCFG", 9)
                        .WithReservedBits(10, 3))
                    .Else(r => r
                        .WithTag("UDRCFG", 9, 2)
                        .WithTag("UDRDET", 11, 2))
                .WithReservedBits(13, 1)
                .WithFlag(14, out receiveDMAEnabled,FieldMode.Read | FieldMode.Set,name: "RXDMAEN")
                .WithFlag(15, out transmitDMAEnabled,FieldMode.Read | FieldMode.Set, name: "TXDMAEN")
                .WithTag("CRCSIZE", 16, 5)
                .WithReservedBits(21, 1)
                .WithTaggedFlag("CRCEN", 22)
                .WithReservedBits(23, 5)
                .WithValueField(28, 3, name: "MBR")
                .If(IsWba)
                    .Then(r => r.WithTaggedFlag("BPASS", 31))
                    .Else(r => r.WithReservedBits(31, 1));

            Registers.Configuration2.Define(registers)
                .WithTag("MSSI", 0, 4)
                .WithTag("MIDI", 4, 4)
                .WithReservedBits(8, 5)
                .If(IsWba)
                    .Then(r => r
                        .WithTaggedFlag("RDIOM", 13)
                        .WithTaggedFlag("RDIOP", 14))
                    .Else(r => r.WithReservedBits(13, 2))
                .WithTaggedFlag("IOSWP", 15)
                .WithReservedBits(16, 1)
                .WithTag("COMM", 17, 2)
                .WithTag("SP", 19, 3)
                // Only master mode is supported
                .WithFlag(22, name: "MASTER", valueProviderCallback: _ => true, writeCallback: (_, value) =>
                    {
                        if(!value)
                        {
                            this.Log(LogLevel.Error, "Attempted to set peripheral into SPI slave mode. Only master mode is supported");
                        }
                    })
                .WithFlag(23, out leastSignificantByteFirst, name: "LSBFRST")
                .WithFlag(24, name: "CPHA")
                .WithFlag(25, name: "CPOL")
                .WithFlag(26, out softwareManagement, name: "SSM")
                .WithReservedBits(27, 1)
                .WithTaggedFlag("SSIOP", 28)
                .WithTaggedFlag("SSOE", 29)
                .WithTaggedFlag("SSOM", 30)
                .WithFlag(31, name: "AFCNTR");

            Registers.InterruptEnable.Define(registers)
                .WithFlag(0, out receiveFifoThresholdInterruptEnable, name: "RXPIE")
                .WithFlag(1, out transmitFifoThresholdInterruptEnable, name: "TXPIE")
                .WithTaggedFlag("DXPIE", 2)
                .WithFlag(3, out endOfTransferInterruptEnable, name: "EOTIE")
                .WithTaggedFlag("TXTFIE", 4)
                .WithFlag(5, name: "UDRIE")
                .WithFlag(6, name: "OVRIE")
                .WithFlag(7, name: "CRCEIE")
                .WithFlag(8, name: "TIFREIE")
                .WithFlag(9, name: "MODFIE")
                .If(IsWba)
                    .Then(r => r.WithReservedBits(10, 1))
                    .Else(r => r.WithTaggedFlag("TSERFIE", 10))
                .WithReservedBits(11, 21)
                .WithWriteCallback((_, __) =>
                {
                    UpdateInterrupts();
                    // We clear EOT here to be compatible with the Zephyr driver: it
                    // waits for EOT to become *0* instead of 1 like the HAL does.
                    // See https://github.com/zephyrproject-rtos/zephyr/blob/a8ed28ab6fc86/drivers/spi/spi_ll_stm32.h#L180
                    //endOfTransfer.Value = false;
                });

            Registers.Status.Define(registers)
                .WithFlag(0, FieldMode.Read, name: "RXP", valueProviderCallback: _ => receiveFifo.Count > 0)
                // We always report that there is space for additional packets
                .WithFlag(1, FieldMode.Read, name: "TXP", valueProviderCallback: _ => true)
                // This flag is equal to RXP && TXP. Since TXP is always true this flag is equal to RXP
                .WithFlag(2, FieldMode.Read, name: "DXP", valueProviderCallback: _ => receiveFifo.Count > 0)
                .WithFlag(3, out endOfTransfer, FieldMode.Read, name: "EOT")
                .WithTaggedFlag("TXTF", 4)
                // Overrun and underrun never occur in this model
                .WithTaggedFlag("UDR", 5)
                .WithTaggedFlag("OVR", 6)
                .WithTaggedFlag("CRCE", 7)
                .WithTaggedFlag("TIFRE", 8)
                .WithTaggedFlag("MODF", 9)
                .If(IsWba)
                    .Then(r => r.WithReservedBits(10, 1))
                    .Else(r => r.WithTaggedFlag("TSERF", 10))
                .WithTaggedFlag("SUSP", 11)
                .WithFlag(12, FieldMode.Read, name: "TXC",
                    valueProviderCallback: _ => transmissionSize.Value == 0 ? transmitFifo.Count == 0 : endOfTransfer.Value)
                .WithValueField(13, 2, name: "RXPLVL",valueProviderCallback: _ =>    {
                                if((ulong)receiveFifo.Count <=  3 )
                                    return (ulong)receiveFifo.Count;
                                else
                                    return 0;
                    })
                .WithValueField(15, 1, name: "RXWNE",valueProviderCallback: _ =>    {
                                if((ulong)receiveFifo.Count > 3)
                                    return 1;
                                else
                                    return 0;
                    })
                .WithValueField(16, 16, FieldMode.Read, name: "CTSIZE", valueProviderCallback: _ => transmissionSize.Value - transmittedPackets);

            Registers.InterruptStatusFlagsClear.Define(registers)
                //.WithReservedBits(0, 3)
                .WithValueField(0, 3, FieldMode.WriteOneToClear, name: "RSVD0_3")
                .WithFlag(3, FieldMode.Write, name: "EOTC", writeCallback: (_, value) =>
                    {
                        if(value)
                        {
                            this.Log(LogLevel.Debug, "Registers.InterruptStatusFlagsClear: EOTC: calling ResetTransmissionState()");
                            ResetTransmissionState();
                        }
                    })
                .WithFlag(4, FieldMode.WriteOneToClear, name: "TXTFC")
                .WithFlag(5, FieldMode.WriteOneToClear, name: "UDRC")
                .WithFlag(6, FieldMode.WriteOneToClear, name: "OVRC")
                .WithFlag(7, FieldMode.WriteOneToClear, name: "CRCEC")
                .WithFlag(8, FieldMode.WriteOneToClear, name: "TIFREC")
                .WithFlag(9, FieldMode.WriteOneToClear, name: "MODFC")
                .If(IsWba)
                    .Then(r => r.WithFlag(10, FieldMode.WriteOneToClear, name: "RSVD10"))
                    .Else(r => r.WithFlag(10, FieldMode.WriteOneToClear, name: "TSERFC"))
                .WithFlag(11, FieldMode.WriteOneToClear, name: "SUSPC")
                //.WithReservedBits(12, 20);
                .WithValueField(12, 20,  FieldMode.WriteOneToClear, name: "RSVD12_20");

            Registers.TransmitData.Define(registers)
                .WithValueField(0, 32, FieldMode.Write, name: "SPI_TXDR", writeCallback: (_, value) =>
                    {
                        this.Log(LogLevel.Debug, "Registers.TransmitData:TXDR: value 0x{0:X}", value);
                        transmitFifo.Enqueue((uint)value);
                        TryTransmitData();
                    });

            Registers.ReceiveData.Define(registers)
                .WithValueField(0, 32, FieldMode.Read, name: "SPI_RXDR", valueProviderCallback: _ =>
                    {
                        if(!receiveFifo.TryDequeue(out var value))
                        {
                            this.Log(LogLevel.Error, "Receive data FIFO is empty. Returning 0");
                            return 0;
                        }
                        this.Log(LogLevel.Debug, "Registers.ReceiveData:RXDR: value 0x{0:X}", value);
                        UpdateInterrupts();
                        return value;
                    });

            if(!IsWba)
            {
                Registers.I2SConfiguration.Define(registers)
                    .WithFlag(0, name: "I2SMOD", valueProviderCallback: _ => false, writeCallback: (_, value) =>
                        {
                            if(value)
                            {
                                this.Log(LogLevel.Error, "Attempted to enable I2S. This mode is not supported");
                            }
                        })
                    .WithTag("I2SCFG[2:0]", 1, 3)
                    .WithTag("I2SSTD[1:0]", 4, 2)
                    .WithReservedBits(6, 1)
                    .WithTaggedFlag("PCMSYNC", 7)
                    .WithTag("DATLEN[1:0]", 8, 2)
                    .WithTaggedFlag("CHLEN", 10)
                    .WithTaggedFlag("CKPOL", 11)
                    .WithTaggedFlag("FIXCH", 12)
                    .WithTaggedFlag("WSINV", 13)
                    .WithTaggedFlag("DATFMT", 14)
                    .WithReservedBits(15, 1)
                    .WithTag("I2SDIV[7:0]", 16, 8)
                    .WithTaggedFlag("ODD", 24)
                    .WithTaggedFlag("MCKOE", 25)
                    .WithReservedBits(26, 6);
            }
        }

        private bool CanWriteToRegister(Registers reg, uint value)
        {
            this.Log(LogLevel.Debug, "CanWriteToRegister: reg {0} value 0x{1:X} peripheralEnabled {2}", reg, value, peripheralEnabled.Value);
            if(peripheralEnabled.Value)
            {
                switch(reg)
                {
                    case Registers.Configuration1:
                    case Registers.Configuration2:
                    case Registers.CRCPolynomial:
                    case Registers.UnderrunData:
                        this.Log(LogLevel.Error, "Attempted to write 0x{0:X} to {0} register while peripheral is enabled", value, reg);
                        ResetTransmissionState();
                        return true;
                }
            }

            return true;
        }

        private void TryTransmitData()
        {
            
            if(!peripheralEnabled.Value || !startTransmission.Value || transmitFifo.Count == 0)
            {
                //this.Log(LogLevel.Warning, "TryTransmitData: early exit");
                return;
            }
            this.Log(LogLevel.Debug, "TryTransmitData: peripheralEnabled {0} startTransmission {1} transmitFifo.Count {2}", peripheralEnabled.Value, startTransmission.Value, transmitFifo.Count);

            // This many bytes are needed to hold all of the packet bits (using ceiling division)
            // The value of the register is one less that the amount of required bits
            var byteCount = (int)packetSizeBits.Value / 8 + 1;
            var bytes = new byte[MaxPacketBytes];
            var reverseBytes = BitConverter.IsLittleEndian && !leastSignificantByteFirst.Value;

            this.Log(LogLevel.Warning, "TryTransmitData: byteCount {0} transmitFifo.Count {1}", byteCount, transmitFifo.Count);

            while(transmitFifo.Count != 0)
            {
                var value = transmitFifo.Dequeue();
                BitHelper.GetBytesFromValue(bytes, 0, value, byteCount, reverseBytes);

                for(var i = 0; i < byteCount; i++)
                {
                    this.Log(LogLevel.Debug, "TryTransmitData: bytes[{0}] TX 0x{1:X}", i, bytes[i]);
                    bytes[i] = RegisteredPeripheral?.Transmit(bytes[i]) ?? 0;
                    this.Log(LogLevel.Debug, "TryTransmitData: bytes[{0}] RX 0x{1:X}", i, bytes[i]);
                }

                this.Log(LogLevel.Debug, "TryTransmitData: setting DMATransmit.Unset()");
                DMATransmit.Unset();

                receiveFifo.Enqueue(BitHelper.ToUInt32(bytes, 0, byteCount, reverseBytes));

                if(receiveDMAEnabled.Value)
                {
                    // This blink is used to signal the DMA that it should perform the peripheral -> memory transaction now
                    // Without this signal DMA will never move data from the receive FIFO to memory
                    // See STM32DMA:OnGPIO
                    DMAReceive.Blink();
                }
                transmittedPackets++;
            }
            this.Log(LogLevel.Debug, "TryTransmitData: after TX: transmissionSize {0} transmittedPackets {1}", transmissionSize.Value, transmittedPackets);

            // In case the transmission size is not specified transmission ends
            // if there are no more packets in the queue
            if(transmittedPackets == transmissionSize.Value || transmissionSize.Value == 0)
            {
                this.Log(LogLevel.Debug, "TryTransmitData: transmissionSize {0} transmittedPackets {1}", transmissionSize.Value, transmittedPackets);

                // Calling the peripheral FinishTransmission here can
                // lose device model state when doing
                // multi-transaction operations when software is
                // managing the chip-select signals. The peripheral
                // FinishTransmission() should only happen when the
                // chip-select is being released.
		if(!softwareManagement.Value)
		{
		    this.Log(LogLevel.Debug, "TryTransmitData: FinishTransmission()");
		    RegisteredPeripheral?.FinishTransmission();
		}

                endOfTransfer.Value = true;
                startTransmission.Value = false;
            }

            UpdateInterrupts();
        }

        private void UpdateInterrupts()
        {
            if(transmitDMAEnabled.Value &&  endOfTransfer.Value )
            {
                //endOfTransferInterruptEnable.Value = transmitDMAEnabled.Value;
                //transmitFifoThresholdInterruptEnable.Value = transmitDMAEnabled.Value;
            }

            var rxp = receiveFifo.Count > 0 && receiveFifoThresholdInterruptEnable.Value;
            var eot = endOfTransfer.Value && endOfTransferInterruptEnable.Value;

            this.Log(LogLevel.Debug, "UpdateInterrupts: receiveFifo.Count {0} endOfTransfer {1} endOfTransferInterruptEnable {2}", receiveFifo.Count, endOfTransfer.Value, endOfTransferInterruptEnable.Value);
            this.Log(LogLevel.Debug, "UpdateInterrupts: rxp {0} eot {1} transmitFifoThresholdInterruptEnable {2}", rxp, eot, transmitFifoThresholdInterruptEnable.Value);

            // TODO: ADD: sequence to indicate DMA transmission (see STM32SPI_Fixed.cs): DMATransmit.Unset(); DMATransmit.Set();
            this.Log(LogLevel.Debug, "UpdateInterrupts: transmitDMAEnabled {0} receiveDMAEnabled {1}", transmitDMAEnabled.Value, receiveDMAEnabled.Value);
            if(transmitDMAEnabled.Value)
            {
                this.Log(LogLevel.Debug, "Update: DMATransmit {0} IsSet {1}", DMATransmit, DMATransmit.IsSet);
                if(!DMATransmit.IsSet)
                {
                    this.Log(LogLevel.Debug, "UpdateInterrupts: transmitDMAEnable and TXE so triggering DMATransmit.Set()");
                    DMATransmit.Unset(); // clear any previous stale state prior to:
                    DMATransmit.Set(); // indicate DMA can perform M2P
                    //IRQ.Set(!endOfTransfer.Value);
                }
            }
            else
            {
                DMATransmit.Unset();
            }

            var irqValue = transmitFifoThresholdInterruptEnable.Value || rxp || eot;
            this.Log(LogLevel.Debug, "Setting IRQ to {0} eot = {1} {2}", irqValue, eot,endOfTransfer.Value);
            IRQ.Set(irqValue);
        }

        private void ResetTransmissionState()
        {
            this.Log(LogLevel.Debug, "ResetTransmissionState");
            endOfTransfer.Value = false;
            startTransmission.Value = false;
            transmittedPackets = 0;
            UpdateInterrupts();
        }

        private readonly DoubleWordRegisterCollection registers;
        private readonly Queue<uint> transmitFifo;
        private readonly Queue<uint> receiveFifo;

        private bool iolockValue;
        private IFlagRegisterField receiveFifoThresholdInterruptEnable;
        private IFlagRegisterField transmitFifoThresholdInterruptEnable;
        private IFlagRegisterField endOfTransferInterruptEnable;
        private IFlagRegisterField endOfTransfer;
        private IFlagRegisterField peripheralEnabled;
        private IFlagRegisterField receiveDMAEnabled;
        private IFlagRegisterField transmitDMAEnabled;
        private IFlagRegisterField startTransmission;
        private IFlagRegisterField leastSignificantByteFirst;
        private IFlagRegisterField softwareManagement;

        private IValueRegisterField transmissionSize;
        private IValueRegisterField packetSizeBits;

        private ulong transmittedPackets;

        private const int MaxPacketBytes = 4;

        private enum Registers
        {
            Control1 = 0x00,                    // SPI_CR1
            Control2 = 0x04,                    // SPI_CR2
            Configuration1 = 0x08,              // SPI_CFG1
            Configuration2 = 0x0C,              // SPI_CFG2
            InterruptEnable = 0x10,             // SPI_IER
            Status = 0x14,                      // SPI_SR
            InterruptStatusFlagsClear = 0x18,   // SPI_IFCR
            AutonomousModeControl = 0x1C,       // SPI_AUTOCR (WBA only)
            TransmitData = 0x20,                // SPI_TXDR
            ReceiveData = 0x30,                 // SPI_RXDR
            CRCPolynomial = 0x40,               // SPI_CRCPOLY
            TransmitterCRC = 0x44,              // SPI_TXCRC
            ReceiverCRC = 0x48,                 // SPI_RXCRC
            UnderrunData = 0x4C,                // SPI_UDRDR
            I2SConfiguration = 0x50,            // SPI_I2SCFGR (non-WBA only)
        }
    }
}
