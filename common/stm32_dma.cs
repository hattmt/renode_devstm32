// Derived from 1.15.0 STM32DMA.cs
//
// Fix TX and RX transfers and maintain DMA pending request state.
// Fix DMA_SxNDTR access.
// Fix transferredSize reset on DMA stream disable.
// Fix DoPeripheralTransfer() to only increment memory when configured.
// Maintain FIFOControl to allow software access.
// Lots of LogLevel.Debug messages added for diagnostics.
//
// Copyright (c) 2010-2023 Antmicro
// Copyright (c) 2011-2015 Realtime Embedded
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Core;
using System.Collections.Generic;
using System.Linq;

namespace Antmicro.Renode.Peripherals.DMA
{
    public sealed class STM32DMA_Fixed : IDoubleWordPeripheral, IKnownSize, IGPIOReceiver, INumberedGPIOOutput
    {
        public STM32DMA_Fixed(IMachine machine)
        {
            streamFinished = new bool[NumberOfStreams];
            streams = new Stream[NumberOfStreams];
            for(var i = 0; i < streams.Length; i++)
            {
                streams[i] = new Stream(this, i);
            }
            this.machine = machine;
            engine = new DmaEngine(machine.GetSystemBus(this));
            Reset();
        }

        public IReadOnlyDictionary<int, IGPIO> Connections
        {
           get
           {
              var i = 0;
              return streams.ToDictionary(x => i++, y => (IGPIO)y.IRQ);
           }
        }

        public long Size
        {
            get
            {
                return 0x400;
            }
        }

        public uint ReadDoubleWord(long offset)
        {
            //this.Log(LogLevel.Debug, "ReadDoubleWord: offset=0x{0:X}", offset);
            switch((Registers)offset)
            {
            case Registers.LowInterruptStatus:
            case Registers.HighInterruptStatus:
                //return HandleInterruptRead((int)(offset/4));
                uint rval = HandleInterruptRead((int)(offset/4));
                this.Log(LogLevel.Debug, "ReadDoubleWord: DMA_xISR: offset=0x{0:X} val 0x{1:X}", offset, rval);
                return rval;
            default:
                if(offset >= StreamOffsetStart && offset <= StreamOffsetEnd)
                {
                    offset -= StreamOffsetStart;
                    //return streams[offset / StreamSize].Read(offset % StreamSize);
                    uint srval = streams[offset / StreamSize].Read(offset % StreamSize);
                    this.Log(LogLevel.Debug, "ReadDoubleWord: offset=0x{0:X} (stream {1}) val 0x{2:X}", (StreamOffsetStart + offset), (offset / StreamSize), srval);
                    return srval;
                }
                this.LogUnhandledRead(offset);
                this.Log(LogLevel.Debug, "ReadDoubleWord: Unhandled: offset=0x{0:X} returning 0x0", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            this.Log(LogLevel.Debug, "WriteDoubleWord: offset 0x{0:X} value 0x{1:X}", offset, value);
            switch((Registers)offset)
            {
            case Registers.LowInterruptClear:
            case Registers.HighInterruptClear:
                HandleInterruptClear((int)((offset - 8)/4), value);
                break;
            default:
                if(offset >= StreamOffsetStart && offset <= StreamOffsetEnd)
                {
                    offset -= StreamOffsetStart;
                    streams[offset / StreamSize].Write(offset % StreamSize, value);
                }
                else
                {
                    this.LogUnhandledWrite(offset, value);
                }
                break;
            }
        }

        public void Reset()
        {
            streamFinished.Initialize();
            foreach(var stream in streams)
            {
                stream.Reset();
            }
        }

        // This OnGPIO() method allows arbitrary stream# indication
        // for the controller instance. However the source peripheral
        // for each stream is actually defined by the DMA_SxCR:CHSEL
        // and the specific STM32 variant controller mapping
        // (e.g. RM0033 rev9 Table 22 and Table 23).
        //
        // CONSIDER:IMPLEMENT: We may be able to use IsConnected and
        // Endpoints and Connect methods to dynamically change the DMA
        // channel stream mapping at run-time to avoid hardwiring in
        // stm32f2.repl

        public void OnGPIO(int number, bool value)
        {
            this.Log(LogLevel.Debug, "OnGPIO: number {0} value {1}", number, value);
            if(number < 0 || number >= streams.Length)
            {
                this.Log(LogLevel.Error, "Attempted to start non-existing DMA stream number: {0}. Maximum value is {1}", number, streams.Length);
                return;
            }

            // Allow (for Set/Unset cases) the current pending DMA request state to be held:
            streams[number].DMARequest = value;

            if(value)
            {
                // direction is private to streams
                //this.Log(LogLevel.Debug, "DMA peripheral request on stream {0} {1} : direction {2}", number, value, streams[number].direction);
                this.Log(LogLevel.Debug, "DMA peripheral request on stream {0} {1} : Enabled {2}", number, value, streams[number].Enabled);
                if(streams[number].Enabled)
                {
                    //streams[number].DoPeripheralTransfer();
                    streams[number].SelectTransfer();
                }
                else
                {
                    // Not really a WARNING since DMA transfer request
                    // is just ignored if DMA stream not enabled:
                    this.Log(LogLevel.Debug, "DMA peripheral request on stream {0} ignored", number);
                }
            }
        }

        private uint HandleInterruptRead(int offset)
        {
            this.Log(LogLevel.Debug, "HandleInterruptRead: offset {0}", offset);
            lock(streamFinished)
            {
                var returnValue = 0u;
                for(var i = 4 * offset; i < 4 * (offset + 1); i++)
                {
                    this.Log(LogLevel.Debug, "HandleInterruptRead: streamFinished[{0}] {1}", i, streamFinished[i]);
                    if(streamFinished[i])
                    {
                        returnValue |= 1u << BitNumberForStream(i - 4 * offset);
                    }
                }
                return returnValue;
            }
        }

        private void HandleInterruptClear(int offset, uint value)
        {
            this.Log(LogLevel.Debug, "HandleInterruptClear: offset {0} value 0x{1:X}", offset, value);
            lock(streamFinished)
            {
                for(var i = 4 * offset; i < 4 * (offset + 1); i++)
                {
                    var bitNo = BitNumberForStream(i - 4 * offset);
                    if((value & (1 << bitNo)) != 0)
                    {
                        this.Log(LogLevel.Debug, "HandleInterruptClear: clearing streamFinished[{0}]", i);
                        streamFinished[i] = false;
                        streams[i].IRQ.Unset();
                    }
                }
            }
        }

        private static int BitNumberForStream(int streamNo)
        {
            switch(streamNo)
            {
            case 0:
                return 5;
            case 1:
                return 11;
            case 2:
                return 21;
            case 3:
                return 27;
            default:
                throw new InvalidOperationException("Should not reach here.");
            }
        }

        private readonly bool[] streamFinished;
        private readonly Stream[] streams;
        private readonly DmaEngine engine;
        private readonly IMachine machine;

        private const int NumberOfStreams = 8;
        private const int StreamOffsetStart = 0x10;
        private const int StreamOffsetEnd = 0xCC;
        private const int StreamSize = 0x18;

        private enum Registers
        {
            LowInterruptStatus = 0x0, // DMA_LISR
            HighInterruptStatus = 0x4, // DMA_HISR
            LowInterruptClear = 0x8, //DMA_LIFCR
            HighInterruptClear = 0xC // DMA_HIFCR
        }

        private class Stream
        {
            public Stream(STM32DMA_Fixed parent, int streamNo)
            {
                this.parent = parent;
                this.streamNo = streamNo;
                IRQ = new GPIO();
            }

            public uint Read(long offset)
            {
                parent.Log(LogLevel.Debug, "STM32DMA:Stream:Read:[{0}] offset=0x{1:X}", streamNo, offset);
                switch((Registers)offset)
                {
                case Registers.Configuration:
                    return HandleConfigurationRead();
                case Registers.NumberOfData:
                    return (uint)numberOfData;
                case Registers.PeripheralAddress:
                    return peripheralAddress;
                case Registers.Memory0Address:
                    return memory0Address;
                case Registers.Memory1Address:
                    return memory1Address;
                case Registers.FIFOControl:
                    return fifoControl;
                default:
                    parent.Log(LogLevel.Warning, "Unexpected read access from not implemented register (offset 0x{0:X}).", offset);
                    return 0;
                }
            }

            public void Write(long offset, uint value)
            {
                parent.Log(LogLevel.Debug, "STM32DMA:Stream:Write:[{0}] offset=0x{1:X} value=0x{2:X}", streamNo, offset, value);
                switch((Registers)offset)
                {
                case Registers.Configuration:
                    HandleConfigurationWrite(value);
                    break;
                case Registers.NumberOfData: // TODO: This register is RO if DMA_SxCR:EN==1
                    // When PSIZE != MSIZE the DMA_SxNDTR value is the
                    // count of PSIZE transfers
                    numberOfData = (int)(value & 0xFFFF);
                    // For CIRC and re-enable we need to reload the original value:
                    numberOfDataLatch = numberOfData;
                    break;
                case Registers.PeripheralAddress: // TODO: This register is RO if DMA_SxCR:EN==1
                    peripheralAddress = value;
                    break;
                case Registers.Memory0Address: // TODO: This register is RO if DMA_SxCR:EN==1
                    memory0Address = value;
                    break;
                case Registers.Memory1Address: // TODO: This register is RO if DMA_SxCR:EN==1
                    memory1Address = value;
                    break;
                case Registers.FIFOControl:
                    if(0x00000000 != (value & 0xFFFFF400))
                    {
                        parent.Log(LogLevel.Warning, "Unexpected FIFOControl write to reserved bits (value 0x{0:X}).", value);
                    }
                    fifoControl = value;
                    break;
                default:
                    parent.Log(LogLevel.Warning, "Unexpected write access to not implemented register (offset 0x{0:X}, value 0x{1:X}).", offset, value);
                    break;
                }
            }

            public GPIO IRQ { get; private set; }

            public void Reset()
            {
                memory0Address = 0u;
                memory1Address = 0u;
                numberOfData = 0;
                numberOfDataLatch = 0;
                transferredSize = 0;
                fifoControl = 0x21; // RM0033 rev 9 9.5.10
                memoryTransferType = TransferType.Byte;
                peripheralTransferType = TransferType.Byte;
                memoryIncrementAddress = false;
                peripheralIncrementAddress = false;
                direction = Direction.PeripheralToMemory;
                interruptOnComplete = false;
                Enabled = false;
                DMARequest = false;
            }

            private Request CreateRequest(int? size = null, int? destinationOffset = null)
            {
                var sourceAddress = 0u;
                var destinationAddress = 0u;
                switch(direction)
                {
                case Direction.PeripheralToMemory:
                case Direction.MemoryToMemory:
                    sourceAddress = peripheralAddress;
                    destinationAddress = memory0Address;
                    break;
                case Direction.MemoryToPeripheral:
                    sourceAddress = memory0Address;
                    destinationAddress = peripheralAddress;
                    break;
                }

                var sourceTransferType = direction == Direction.PeripheralToMemory ? peripheralTransferType : memoryTransferType;
                var destinationTransferType = direction == Direction.MemoryToPeripheral ? peripheralTransferType : memoryTransferType;
                var incrementSourceAddress = direction == Direction.PeripheralToMemory ? peripheralIncrementAddress : memoryIncrementAddress;
                var incrementDestinationAddress = direction == Direction.MemoryToPeripheral ? peripheralIncrementAddress : memoryIncrementAddress;
                parent.Log(LogLevel.Debug, "CreateRequest: Request: source 0x{0:X} destination 0x{1:X} size {2} readTransferType {3} writeTransferType {4} incrementReadAddress {5} incrementWriteAddress {6}", sourceAddress, (uint)(destinationAddress + (destinationOffset ?? 0)), size ?? numberOfData, sourceTransferType, destinationTransferType, incrementSourceAddress, incrementDestinationAddress);
                return new Request(sourceAddress, (uint)(destinationAddress + (destinationOffset ?? 0)), size ?? numberOfData, sourceTransferType, destinationTransferType,
                        incrementSourceAddress, incrementDestinationAddress);
            }


            // If doing 16- or 32-bit DMA transfers (PSIZE/MSIZE) then
            // we need to treat a single transfer as 1-item of PSIZE.
	    //
            // If circular then we do not auto-disable but should
            // reset numberOfData = numberOfDataLatch and continue DMA
            // requests

            public void DoTransfer()
            {
                parent.Log(LogLevel.Debug, "DoTransfer: entry: memoryTransferType {0} transferredSize {1} direction {2}", (int)memoryTransferType, transferredSize, direction);

                var request = CreateRequest();
                parent.Log(LogLevel.Debug, "DoTransfer: request.Size {0}", request.Size);
                if(request.Size > 0)
                {
                    lock(parent.streamFinished)
                    {
                        parent.engine.IssueCopy(request);
                        if (circular)
                        {
                            numberOfData = numberOfDataLatch;
                            parent.Log(LogLevel.Debug, "DoTransfer: circular: reset numberOfData {0}", numberOfData);
                        }
                        else
                        {
                            numberOfData -= (request.Size / (int)peripheralTransferType);
                            Enabled = false;
                        }
                        parent.streamFinished[streamNo] = true;
                        parent.Log(LogLevel.Debug, "DoTransfer: setting streamFinished[{0}] true : interruptOnComplete {1} numberOfData {2}", streamNo, interruptOnComplete, numberOfData);
                        if(interruptOnComplete)
                        {
                            parent.machine.LocalTimeSource.ExecuteInNearestSyncedState(_ => IRQ.Set());
                        }
                    }
                }
            }

            public void DoPeripheralTransfer()
            {
                parent.Log(LogLevel.Debug, "DoPeripheralTransfer: entry: memoryTransferType {0} transferredSize {1} direction {2}", (int)memoryTransferType, transferredSize, direction);
                // NOTE: Since we are only called for P2M the
                // transferredSize here is used as the
                // "destinationOffset" index when incrementing the
                // memory address
                var request = CreateRequest((int)memoryTransferType, (memoryIncrementAddress ? transferredSize : 0));
                transferredSize += (int)memoryTransferType;
                parent.Log(LogLevel.Debug, "DoPeripheralTransfer: request.Size {0} transferredSize {1} : numberOfData {2}", request.Size, transferredSize, numberOfData);
                if(request.Size > 0)
                {
                    lock(parent.streamFinished)
                    {
                        parent.engine.IssueCopy(request);
                        parent.Log(LogLevel.Debug, "DoPeripheralTransfer: request.Size {0} transferredSize {1} numberOfData {2}  peripheralTransferType {3} ({4})", request.Size, transferredSize, numberOfData, peripheralTransferType, (numberOfData * (int)peripheralTransferType));
                        // TODO:CONSIDER:OPTIMISE: We should be able
                        // to cache the (numberOfData * (int)peripheralTransferType)
                        // value when the DMA_SxCR is written since at
                        // that point it is fixed; and it will avoid
                        // the repeated multiplication operation here.
                        if(transferredSize == (numberOfData * (int)peripheralTransferType))
                        {
                            if (circular)
                            {
                                numberOfData = numberOfDataLatch;
                                parent.Log(LogLevel.Debug, "DoPeripheralTransfer: circular: reset numberOfData {0}", numberOfData);
                            }
                            else
                            {
                                numberOfData -= (transferredSize / (int)peripheralTransferType);
                                Enabled = false;
                            }
                            parent.Log(LogLevel.Debug, "DoPeripheralTransfer: setting streamFinished[{0}] true : interruptOnComplete {1} numberOfData {2}", streamNo, interruptOnComplete, numberOfData);
                            parent.streamFinished[streamNo] = true;
                            transferredSize = 0;
                            if(interruptOnComplete)
                            {
                                parent.machine.LocalTimeSource.ExecuteInNearestSyncedState(_ => IRQ.Set());
                            }
                        }
                    }
                }
            }

            public void SelectTransfer()
            {
                parent.Log(LogLevel.Debug, "SelectTransfer: direction {0}", direction);

                if((direction == Direction.MemoryToMemory) || (direction == Direction.MemoryToPeripheral))
                {
                    DoTransfer();
                }
                if(direction == Direction.PeripheralToMemory)
                {
                    DoPeripheralTransfer();
                }
            }

            public bool Enabled { get; private set; }

            public bool DMARequest { get; set; }

            private uint HandleConfigurationRead()
            {
                var returnValue = 0u;
                returnValue |= (uint)(channel << 25);
                returnValue |= (uint)(priority << 16);

                returnValue |= FromTransferType(memoryTransferType) << 13;
                returnValue |= FromTransferType(peripheralTransferType) << 11;
                returnValue |= memoryIncrementAddress ? (1u << 10) : 0u;
                returnValue |= peripheralIncrementAddress ? (1u << 9) : 0u;
                returnValue |= ((uint)direction) << 6;
                returnValue |= interruptOnComplete ? (1u << 4) : 0u;
                // regarding enable bit - our transfer is always finished
                parent.Log(LogLevel.Debug, "HandleConfigurationRead:[{0}] returning 0x{1:X}", streamNo, returnValue);
                return returnValue;
            }

            private void HandleConfigurationWrite(uint value)
            {
                parent.Log(LogLevel.Debug, "HandleConfigurationWrite:[{0}] value 0x{1:X}", streamNo, value);
                // we ignore channel selection and priority
                channel = (byte)((value >> 25) & 7);
                priority = (byte)((value >> 16) & 3);

                memoryTransferType = ToTransferType(value >> 13); // MSIZE
                peripheralTransferType = ToTransferType(value >> 11); // PSIZE
                memoryIncrementAddress = (value & (1 << 10)) != 0;
                peripheralIncrementAddress = (value & (1 << 9)) != 0;
                direction = (Direction)((value >> 6) & 3);
                interruptOnComplete = (value & (1 << 4)) != 0;

                // CIRC bit-8 support since used by ADC and Serial DMA
                //      When CIRC mode is active the number of data
                //      items to be transferred is automatically
                //      reloaded with the initial value programmed
                //
                //      When the PFCTRL==1 and stream EN==1 then CIRC is forced by H/W to 0
                //      It is auto forced to 1 if DBM bit is set as soon as stream is enabled: EN=1
                circular = (value & (1 << 8)) != 0;

                // TODO:CONSIDER: DBM bit-18 for double buffered mode
                // - e.g. eCosPro serial STM32 driver may use double-buffering and half-int support

                // 0E037ED5     0000 1110 0000 0011 0111 1110 1101 0101         <- is inverted
                //              1111 0001 1111 1100 1000 0001 0010 1010         <- the original mask
                // which includes
                //      bit1      DMEIE
                //      bit3      HTIE
                //      bit5      PFCTRL
                //      bit8      CIRC
                //      bit15     PINCOS
                //      bit18     DBM
                //      bit19     CT
                //      bits21-22 PBURST
                //      bits23-24 MBURST

                // we ignore transfer error interrupt enable as we never post errors
                if((value & ~0xE037FD5) != 0)
                {
                    parent.Log(LogLevel.Warning, "Channel {0}: unsupported bits written to configuration register. Value is 0x{1:X}.", streamNo, value);
                }

                if((value & 1) != 0)
                {
                    numberOfData = numberOfDataLatch; // as per RM0033 rev9 9.5.6

                    parent.Log(LogLevel.Debug, "HandleConfigurationWrite:[{0}] Enabled : direction {1} DMARequest {2} numberOfData {3} memoryIncrementAddress {4} peripheralIncremenetAddress {5}", streamNo, direction, DMARequest, numberOfData, memoryIncrementAddress, peripheralIncrementAddress);

                    if((direction != Direction.PeripheralToMemory) && DMARequest)
                    {
                        DoTransfer();
                    }
                    else
                    {
                        Enabled = true;
                    }
                }
                else
                {
                    parent.Log(LogLevel.Debug, "HandleConfigurationWrite: cstream {0} disable: Setting transferredSize to 0", streamNo);
                    transferredSize = 0;
                }
            }

            private TransferType ToTransferType(uint dataSize)
            {
                dataSize &= 3;
                switch(dataSize)
                {
                case 0:
                    return TransferType.Byte;
                case 1:
                    return TransferType.Word;
                case 2:
                    return TransferType.DoubleWord;
                default:
                    parent.Log(LogLevel.Warning, "Stream {0}: Non existitng possible value written as data size.", streamNo);
                    return TransferType.Byte;
                }
            }

            private static uint FromTransferType(TransferType transferType)
            {
                switch(transferType)
                {
                case TransferType.Byte:
                    return 0;
                case TransferType.Word:
                    return 1;
                case TransferType.DoubleWord:
                    return 2;
                }
                throw new InvalidOperationException("Should not reach here.");
            }

            private uint memory0Address;
            private uint memory1Address;
            private uint peripheralAddress;
            private uint fifoControl;
            private int numberOfData;
            private int numberOfDataLatch;
            private int transferredSize;
            private TransferType memoryTransferType;
            private TransferType peripheralTransferType;
            private bool memoryIncrementAddress;
            private bool peripheralIncrementAddress;
            private bool circular;
            private Direction direction;
            private bool interruptOnComplete;
            private byte channel;
            private byte priority;

            private readonly STM32DMA_Fixed parent;
            private readonly int streamNo;

            private enum Registers
            {
                Configuration = 0x0, // DMA_SxCR
                NumberOfData = 0x4, // DMA_SxNDTR
                PeripheralAddress = 0x8, // DMA_SxPAR
                Memory0Address = 0xC, // DMA_SxM0AR
                Memory1Address = 0x10, // DMA_SxM1AR
                FIFOControl = 0x14, // DMA_SxFCR
            }

            private enum Direction : byte
            {
                PeripheralToMemory = 0,
                MemoryToPeripheral = 1,
                MemoryToMemory = 2
            }
        }
    }
}
