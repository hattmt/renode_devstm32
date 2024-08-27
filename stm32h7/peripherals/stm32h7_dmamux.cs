// Copyright (c) 2024 eCosCentric Ltd

using System;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Exceptions;
using System.Collections.Generic;
using System.Linq;

// same DMAMUX controller for RM0468 (H72/H73) and RM0433 (H74/H75)

// TODO:CONSIDER: RM0432 Rev5 for STM32L4+

//                                  // DMAMUX1                          // DMAMUX2
//                                  // RM0468 Rev3 // RM0433 Rev7       //
// # out request channels           //    16       //      16           //    8
// # request generator channels     //     8       //       8           //    8
// # request trigger inputs         //     8       //       8           //   32
// # synchronisation inputs         //     8       //       8           //   16
// # peripheral request inputs      //   129       //     107           //   12

// DMAMUX1 channels 0..7  connected to DMA1 channels 0..7
// DMAMUX1 channels 8..15 connected to DMA2 channels 0..7

// DMAMUX2 channels 0..7  connected to BDMA channels 0..7

//=============================================================================

// From stm32 var_io.h for H7 and matches RM0468
//
// #define CYGHWR_HAL_STM32_DMAMUX_MEM2MEM          0
// #define CYGHWR_HAL_STM32_DMAMUX_GENERATOR0       1
// #define CYGHWR_HAL_STM32_DMAMUX_GENERATOR1       2
// #define CYGHWR_HAL_STM32_DMAMUX_GENERATOR2       3
// #define CYGHWR_HAL_STM32_DMAMUX_GENERATOR3       4
// #define CYGHWR_HAL_STM32_DMAMUX_GENERATOR4       5
// #define CYGHWR_HAL_STM32_DMAMUX_GENERATOR5       6
// #define CYGHWR_HAL_STM32_DMAMUX_GENERATOR6       7
// #define CYGHWR_HAL_STM32_DMAMUX_GENERATOR7       8
// #define CYGHWR_HAL_STM32_DMAMUX_ADC1             9
// #define CYGHWR_HAL_STM32_DMAMUX_ADC2             10
// #define CYGHWR_HAL_STM32_DMAMUX_TIM1_CH1         11
// #define CYGHWR_HAL_STM32_DMAMUX_TIM1_CH2         12
// #define CYGHWR_HAL_STM32_DMAMUX_TIM1_CH3         13
// #define CYGHWR_HAL_STM32_DMAMUX_TIM1_CH4         14
// #define CYGHWR_HAL_STM32_DMAMUX_TIM1_UP          15
// #define CYGHWR_HAL_STM32_DMAMUX_TIM1_TRIG        16
// #define CYGHWR_HAL_STM32_DMAMUX_TIM1_COM         17
// #define CYGHWR_HAL_STM32_DMAMUX_TIM2_CH1         18
// #define CYGHWR_HAL_STM32_DMAMUX_TIM2_CH2         19
// #define CYGHWR_HAL_STM32_DMAMUX_TIM2_CH3         20
// #define CYGHWR_HAL_STM32_DMAMUX_TIM2_CH4         21
// #define CYGHWR_HAL_STM32_DMAMUX_TIM2_UP          22
// #define CYGHWR_HAL_STM32_DMAMUX_TIM3_CH1         23
// #define CYGHWR_HAL_STM32_DMAMUX_TIM3_CH2         24
// #define CYGHWR_HAL_STM32_DMAMUX_TIM3_CH3         25
// #define CYGHWR_HAL_STM32_DMAMUX_TIM3_CH4         26
// #define CYGHWR_HAL_STM32_DMAMUX_TIM3_UP          27
// #define CYGHWR_HAL_STM32_DMAMUX_TIM3_TRIG        28
// #define CYGHWR_HAL_STM32_DMAMUX_TIM4_CH1         29
// #define CYGHWR_HAL_STM32_DMAMUX_TIM4_CH2         30
// #define CYGHWR_HAL_STM32_DMAMUX_TIM4_CH3         31
// #define CYGHWR_HAL_STM32_DMAMUX_TIM4_UP          32
// #define CYGHWR_HAL_STM32_DMAMUX_I2C1_RX          33
// #define CYGHWR_HAL_STM32_DMAMUX_I2C1_TX          34
// #define CYGHWR_HAL_STM32_DMAMUX_I2C2_RX          35
// #define CYGHWR_HAL_STM32_DMAMUX_I2C2_TX          36
// #define CYGHWR_HAL_STM32_DMAMUX_SPI1_RX          37
// #define CYGHWR_HAL_STM32_DMAMUX_SPI1_TX          38
// #define CYGHWR_HAL_STM32_DMAMUX_SPI2_RX          39
// #define CYGHWR_HAL_STM32_DMAMUX_SPI2_TX          40
// #define CYGHWR_HAL_STM32_DMAMUX_USART1_RX        41
// #define CYGHWR_HAL_STM32_DMAMUX_USART1_TX        42
// #define CYGHWR_HAL_STM32_DMAMUX_USART2_RX        43
// #define CYGHWR_HAL_STM32_DMAMUX_USART2_TX        44
// #define CYGHWR_HAL_STM32_DMAMUX_USART3_RX        45
// #define CYGHWR_HAL_STM32_DMAMUX_USART3_TX        46
// #define CYGHWR_HAL_STM32_DMAMUX_TIM8_CH1         47
// #define CYGHWR_HAL_STM32_DMAMUX_TIM8_CH2         48
// #define CYGHWR_HAL_STM32_DMAMUX_TIM8_CH3         49
// #define CYGHWR_HAL_STM32_DMAMUX_TIM8_CH4         50
// #define CYGHWR_HAL_STM32_DMAMUX_TIM8_UP          51
// #define CYGHWR_HAL_STM32_DMAMUX_TIM8_TRIG        52
// #define CYGHWR_HAL_STM32_DMAMUX_TIM8_COM         53
// NOTE: 54 reserved on RM0468 and RM0433
// #define CYGHWR_HAL_STM32_DMAMUX_TIM5_CH1         55
// #define CYGHWR_HAL_STM32_DMAMUX_TIM5_CH2         56
// #define CYGHWR_HAL_STM32_DMAMUX_TIM5_CH3         57
// #define CYGHWR_HAL_STM32_DMAMUX_TIM5_CH4         58
// #define CYGHWR_HAL_STM32_DMAMUX_TIM5_UP          59
// #define CYGHWR_HAL_STM32_DMAMUX_TIM5_TRIG        60
// #define CYGHWR_HAL_STM32_DMAMUX_SPI3_RX          61
// #define CYGHWR_HAL_STM32_DMAMUX_SPI3_TX          62
// #define CYGHWR_HAL_STM32_DMAMUX_UART4_RX         63
// #define CYGHWR_HAL_STM32_DMAMUX_UART4_TX         64
// #define CYGHWR_HAL_STM32_DMAMUX_UART5_RX         65
// #define CYGHWR_HAL_STM32_DMAMUX_UART5_TX         66
// #define CYGHWR_HAL_STM32_DMAMUX_DAC1_CH1         67
// #define CYGHWR_HAL_STM32_DMAMUX_DAC1_CH2         68
// #define CYGHWR_HAL_STM32_DMAMUX_TIM6_UP          69
// #define CYGHWR_HAL_STM32_DMAMUX_TIM7_UP          70
// #define CYGHWR_HAL_STM32_DMAMUX_USART6_RX        71
// #define CYGHWR_HAL_STM32_DMAMUX_USART6_TX        72
// #define CYGHWR_HAL_STM32_DMAMUX_I2C3_RX          73
// #define CYGHWR_HAL_STM32_DMAMUX_I2C3_TX          74
// #define CYGHWR_HAL_STM32_DMAMUX_DCMI             75
// #define CYGHWR_HAL_STM32_DMAMUX_CRYP_IN          76
// #define CYGHWR_HAL_STM32_DMAMUX_CRYP_OUT         77
// #define CYGHWR_HAL_STM32_DMAMUX_HASH_IN          78
// #define CYGHWR_HAL_STM32_DMAMUX_UART7_RX         79
// #define CYGHWR_HAL_STM32_DMAMUX_UART7_TX         80
// #define CYGHWR_HAL_STM32_DMAMUX_UART8_RX         81
// #define CYGHWR_HAL_STM32_DMAMUX_UART8_TX         82
// #define CYGHWR_HAL_STM32_DMAMUX_SPI4_RX          83
// #define CYGHWR_HAL_STM32_DMAMUX_SPI4_TX          84
// #define CYGHWR_HAL_STM32_DMAMUX_SPI5_RX          85
// #define CYGHWR_HAL_STM32_DMAMUX_SPI5_TX          86
// #define CYGHWR_HAL_STM32_DMAMUX_SAI1_A           87
// #define CYGHWR_HAL_STM32_DMAMUX_SAI1_B           88
// #define CYGHWR_HAL_STM32_DMAMUX_SAI2_A           89
// #define CYGHWR_HAL_STM32_DMAMUX_SAI2_B           90
// #define CYGHWR_HAL_STM32_DMAMUX_SWPMI_RX         91
// #define CYGHWR_HAL_STM32_DMAMUX_SWPMI_TX         92
// #define CYGHWR_HAL_STM32_DMAMUX_SPDIF_RX_DT      93
// #define CYGHWR_HAL_STM32_DMAMUX_SPDIF_RX_CS      94
// #define CYGHWR_HAL_STM32_DMAMUX_RSVD_95          95          RM0433 is HR_REQ(1)
// #define CYGHWR_HAL_STM32_DMAMUX_RSVD_96          96          RM0433 is HR_REQ(2)
// #define CYGHWR_HAL_STM32_DMAMUX_RSVD_97          97          RM0433 is HR_REQ(3)
// #define CYGHWR_HAL_STM32_DMAMUX_RSVD_98          98          RM0433 is HR_REQ(4)
// #define CYGHWR_HAL_STM32_DMAMUX_RSVD_99          99          RM0433 is HR_REQ(5)
// #define CYGHWR_HAL_STM32_DMAMUX_RSVD_100        100          RM0433 is HR_REQ(6)
// #define CYGHWR_HAL_STM32_DMAMUX_DFSDM1_FLT0     101
// #define CYGHWR_HAL_STM32_DMAMUX_DFSDM1_FLT1     102
// #define CYGHWR_HAL_STM32_DMAMUX_DFSDM1_FLT2     103
// #define CYGHWR_HAL_STM32_DMAMUX_DFSDM1_FLT3     104
// #define CYGHWR_HAL_STM32_DMAMUX_TIM15_CH1       105
// #define CYGHWR_HAL_STM32_DMAMUX_TIM15_UP        106
// #define CYGHWR_HAL_STM32_DMAMUX_TIM15_TRIG      107
// #define CYGHWR_HAL_STM32_DMAMUX_TIM15_COM       108
// #define CYGHWR_HAL_STM32_DMAMUX_TIM16_CH1       109
// #define CYGHWR_HAL_STM32_DMAMUX_TIM16_UP        110
// #define CYGHWR_HAL_STM32_DMAMUX_TIM17_CH1       111
// #define CYGHWR_HAL_STM32_DMAMUX_TIM17_UP        112
// #define CYGHWR_HAL_STM32_DMAMUX_RSVD_113        113          RM0433 is SAI3_A
// #define CYGHWR_HAL_STM32_DMAMUX_RSVD_114        114          RM0433 is SAI3_B
// #define CYGHWR_HAL_STM32_DMAMUX_ADC3            115
// #define CYGHWR_HAL_STM32_DMAMUX_UART9_RX        116          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_UART9_TX        117          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_USART10_RX      118          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_USART10_TX      119          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_FMAC_RD         120          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_FMAC_WR         121          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_CORDIC_RD       122          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_CORDIC_WR       123          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_I2C5_RX         124          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_I2C5_TX         125          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_TIM23_CH1       126          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_TIM23_CH2       127          RM0433 is reserved
// #define CYGHWR_HAL_STM32_DMAMUX_TIM23_CH3       128          RM0433 does not have anything >=128
// #define CYGHWR_HAL_STM32_DMAMUX_TIM23_CH4       129          "
// #define CYGHWR_HAL_STM32_DMAMUX_TIM23_UP        130          "
// #define CYGHWR_HAL_STM32_DMAMUX_TIM23_TRIG      131          "
// #define CYGHWR_HAL_STM32_DMAMUX_TIM24_CH1       132          "
// #define CYGHWR_HAL_STM32_DMAMUX_TIM24_CH2       133          "
// #define CYGHWR_HAL_STM32_DMAMUX_TIM24_CH3       134          "
// #define CYGHWR_HAL_STM32_DMAMUX_TIM24_CH4       135          "
// #define CYGHWR_HAL_STM32_DMAMUX_TIM24_UP        136          "
// #define CYGHWR_HAL_STM32_DMAMUX_TIM24_TRIG      137          "

// Not defined in var_io.h but RM0468 Rev3 Table 121 / RM0433 Rev7 Table 124	(both parts have same DMAMUX2 multiplexer mappings)
//  0 MEM2MEM          	// not in table but implied
//  1 dmamux2_req_gen0
//  2 dmamux2_req_gen1
//  3 dmamux2_req_gen2
//  4 dmamux2_req_gen3
//  5 dmamux2_req_gen4
//  6 dmamux2_req_gen5
//  7 dmamux2_req_gen6
//  8 dmamux2_req_gen7
//  9 lpuart1_rx_dma
// 10 lpuart1_tx_dma
// 11 spi6_rx_dma
// 12 spi6_tx_dma
// 13 i2c4_rx_dma
// 14 i2c4_tx_dma
// 15 sai4_a_dma
// 16 sai4_b_dma
// 17 adc3_dma
// 18 Reserved
// 19 Reserved
// 20 Reserved
// 21 Reserved
// 22 Reserved
// 23 Reserved
// 24 Reserved
// 25 Reserved
// 26 Reserved
// 27 Reserved
// 28 Reserved
// 29 Reserved
// 30 Reserved
// 31 Reserved
// 32 Reserved

// TODO: See SYSCFG which maps sources through to NVIC
// - this does something similar mapping peripheral input requests to DMA/BDMA channels when the config matches

namespace Antmicro.Renode.Peripherals.DMA
{
    // peripheral can be accessed via Byte and HalfWord
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]

    // STALE:TODO: do we want to be a BasicDoubleWordPeripheral (since that provides the Read/WriteDoubleWord methods
    // STALE:TODO: or do we want to be like GPIOPort.cs and provide our own registers map
    // Latter is useful for extra diag to track read/writes to a controller

    public sealed class STM32H7_DMAMUX : IDoubleWordPeripheral, IKnownSize, IGPIOReceiver, INumberedGPIOOutput
    {
        public STM32H7_DMAMUX(Machine machine, uint numberOfOutRequestChannels = 16, uint numberOfRequestTriggerInputs = 8, uint numberOfSynchronisationInputs = 8)
        {
            this.Log(LogLevel.Debug, "numberOfOutRequestChannels {0}", numberOfOutRequestChannels);

            // TODO: decide if we need to distinguish stm32Family

            if((8 != numberOfOutRequestChannels) && (16 != numberOfOutRequestChannels))
            {
                throw new ConstructionException("Expecting numberOfOutRequestChannels 8 or 16");
            }

	    this.numberOfOutRequestChannels = numberOfOutRequestChannels;
	    this.numberOfRequestTriggerInputs = numberOfRequestTriggerInputs;
	    this.numberOfSynchronisationInputs = numberOfSynchronisationInputs;

	    requests = new RequestLine[numberOfOutRequestChannels];
	    for(var i = 0; (i < requests.Length); i++)
	    {
		requests[i] = new RequestLine(this, i);
	    }
	    RequestLineOffsetEnd = ((numberOfOutRequestChannels * 4) - 4);

	    generators = new RequestGenerator[numberOfRequestGenerators];
	    for(var i = 0; (i < generators.Length); i++)
	    {
		generators[i] = new RequestGenerator(this, i);
	    }

            syncOverrunEventFlag = new bool[numberOfOutRequestChannels];

	    this.machine = machine;

            DefineRegisters();
            Reset();
        }

	public IReadOnlyDictionary<int, IGPIO> Connections
	{
	    get
	    {
		var i = 0;
		this.Log(LogLevel.Debug, "Connections: TODO: multiplexer inputs mapped to individual DMAMUX_CxCR configuration 0..(numberOfOutRequestChannels-1)");
		return requests.ToDictionary(x => i++, y => (IGPIO)y.eventSignal);
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
            //uint value = registers.Read(offset);
            uint value = 0x00000000;
	    switch((Registers)offset)
	    {
	    case Registers.IntChannelStatus:
	    case Registers.IntClearFlag:
	    case Registers.RequestGeneratorIntStatus:
	    case Registers.RequestGeneratorIntClearFlag:
		// TODO:
		value = 0xDEADBEEF;
		break;
	    default:
		if((offset >= RequestLineOffsetStart) && (offset <= RequestLineOffsetEnd))
		{
		    offset -= RequestLineOffsetStart;
		    value = requests[offset / 4].Read(offset % 4);
		}
		else if((offset >= RequestGeneratorsOffsetStart) && (offset <= RequestGeneratorsOffsetEnd))
		{
		    offset -= RequestGeneratorsOffsetStart;
		    value = generators[offset / 4].Read(offset % 4);
		}
		else
		{
		    this.LogUnhandledRead(offset);
		}
		break;
	    }
            this.Log(LogLevel.Debug, "ReadDoubleWord: offset=0x{0:X} value 0x{1:X}", offset, value);
	    return value;
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            this.Log(LogLevel.Debug, "WriteDoubleWord: offset=0x{0:X} value 0x{1:X}", offset, value);
            //registers.Write(offset, value);
	    switch((Registers)offset)
	    {
	    case Registers.IntChannelStatus:
	    case Registers.IntClearFlag:
	    case Registers.RequestGeneratorIntStatus:
	    case Registers.RequestGeneratorIntClearFlag:
		// TODO:
		break;
	    default:
		if((offset >= RequestLineOffsetStart) && (offset <= RequestLineOffsetEnd))
		{
		    offset -= RequestLineOffsetStart;
		    requests[offset / 4].Write((offset % 4), value);
		    break;
		}
		else if((offset >= RequestGeneratorsOffsetStart) && (offset <= RequestGeneratorsOffsetEnd))
		{
		    offset -= RequestGeneratorsOffsetStart;
		    generators[offset / 4].Write((offset % 4), value);
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
            this.Log(LogLevel.Debug, "Reset");
            // TODO
	    foreach(var requestLine in requests)
	    {
		requestLine.Reset();
	    }
	    foreach(var requestGenerator in generators)
	    {
		requestGenerator.Reset();
	    }
        }

        public void OnGPIO(int number, bool value)
        {
            this.Log(LogLevel.Debug, "OnGPIO: number {0} value {1}", number, value);
            // TODO: we expect "number" to be the RM0468 Table 118 for DMAMUX1 or Table 121 for DMAMUX2 input multiplexor value
	    // e.g. DMAMUX1 38 for spi1_tx_dma indicating a DMA request from the peripheral
	    // we could check that the number is configured and ignore if not
	    // if configured we should pass request onto the relevant DMA controller and propogate DMA channel events (our output "GPIO" signals)
        }

        private void DefineRegisters()
        {
            // TODO: see STM32_GPIOPort_Fixed,cs for bitmap help

            //Registers.IntChannelStatus.Define(this, name: "DMAMUX_CSR")
            //    .WithValueField(0, numberOfOutRequestChannels, FieldMode.Read, valueProviderCallback: _ => BitHelper.GetValueFromBitsArray(syncOverrunEventFlag), name: "SOF")
            //    .WithReservedBits(numberOfOutRequestChannels, (32 - numberOfOutRequestChannels));

            // Could not use numberOfOutRequestChannels in the Define
            //if(16 == numberOfOutRequestChannels)
            //{
            //    Registers.IntChannelStatus.Define(this, name: "DMAMUX_CSR")
            //        .WithValueField(0, 16, FieldMode.Read, valueProviderCallback: _ => BitHelper.GetValueFromBitsArray(syncOverrunEventFlag), name: "SOF")
            //        .WithReservedBits(16, 16);
            //}
            //else
            //{
            //    Registers.IntChannelStatus.Define(this, name: "DMAMUX_CSR")
            //        .WithValueField(0, 8, FieldMode.Read, valueProviderCallback: _ => BitHelper.GetValueFromBitsArray(syncOverrunEventFlag), name: "SOF")
            //        .WithReservedBits(8, 24);
            //}

            // TODO: Registers.IntClearFlag write only

            // TODO: Registers.RequestGeneratorIntStatus read-only 8-bits
            // TODO: Registers.RequestGeneratorIntClearFlag write-only 8-bits
        }

        private enum Registers
        {
            // TODO: vector of [numberOfOutRequestChannels] from 0x00 // DMAMUXx_CyCR

            IntChannelStatus = 0x80, // DMAMUXx_CSR
            IntClearFlag     = 0x84, // DMAMUXx_CFR

            // TODO: vector of [8] from 0x100 // DMAMUXx_RGyCR

            RequestGeneratorIntStatus = 0x140, // DMAMUXx_RGSR
            RequestGeneratorIntClearFlag = 0x144 // DMAMUXx_RGCFR
        }

	private const uint numberOfRequestGenerators = 8;

        private readonly uint numberOfOutRequestChannels;
        private readonly uint numberOfSynchronisationInputs;
	private readonly uint numberOfRequestTriggerInputs;

	private const long RequestLineOffsetStart = 0x00;
	private long RequestLineOffsetEnd;

	private const long RequestGeneratorsOffsetStart = 0x100;
	private const long RequestGeneratorsOffsetEnd = 0x11C;

        private readonly Machine machine;
        private readonly RequestLine[] requests;
        private readonly RequestGenerator[] generators;

        private bool[] syncOverrunEventFlag;

        private class RequestLine
	{
	    public RequestLine(STM32H7_DMAMUX parent, int requestNo)
	    {
		this.parent = parent;
		this.requestNo = requestNo;
		eventSignal = new GPIO();
		//registers = new DoubleWordRegisterCollection(this); // would need to be IDoubleWordPeripheral to do this
		//SetupRegisters();
		Reset();
	    }

	    public void Reset()
	    {
                parent.Log(LogLevel.Debug, "STM32H7_DMAMUX:RequestLine:Reset:[{0}]", requestNo);
		//registers.Reset();
		dmaRequestId = 0;
		synchronisationId = 0;
		synchronisationOverrunInterruptEnable = false;
		eventGenerationEnable = false;
		synchronisationEnable = false;
		synchronisationPolarity = SynchronisationPolarity.NoEvent;
		numberOfDMARequests = 1;
	    }

	    // TODO: add Registers Define support to have it manage the fields within the register
	    public uint Read(long offset)
	    {
		parent.Log(LogLevel.Debug, "STM32H7_DMAMUX:RequestLine:Read[{0}] offset 0x{1:X}", requestNo, offset);
		//return registers.Read(offset);
		switch((Registers)offset)
		{
		case Registers.Configuration:
		    return HandleConfigurationRead();
		default:
		    parent.Log(LogLevel.Warning, "Unexpected read access from not implemented register (offset 0x{0:X}).", offset);
		    return 0x00000000;
		}
	    }

            public void Write(long offset, uint value)
            {
                parent.Log(LogLevel.Debug, "STM32H7_DMAMUX:RequestLine:Write:[{0}] offset 0x{1:X} value 0x{2:X}", requestNo, offset, value);
		//registers.Write(offset, value);
		switch((Registers)offset)
		{
		case Registers.Configuration:
		    HandleConfigurationWrite(value);
		    break;
                default:
                    parent.Log(LogLevel.Warning, "Unexpected write access to not implemented register (offset 0x{0:X}, value 0x{1:X}).", offset, value);
                    break;
		}
	    }

	    public GPIO eventSignal { get; private set; }

	    //private void SetupRegisters()
	    //{
            //   // TODO:IMPLEMENT: Handle read/write of fields (e.g. NBREQ being stored as 1 less than DMA transfer count
            //    Registers.Configuration.Define(registers)
            //        .WithValueField(0, 8, name: "DMAREQ_ID") // TODO: RM0468 only 5-bits for DMAMUX2
            //        .WithFlag(8, name: "SOIE")
            //        .WithFlag(9, name: "EGE")
            //        .WithReservedBits(10, 6)
            //        .WithFlag(16, name: "SE")
            //        .WithEnumField<DoubleWordRegister, SynchronisationPolarity>(17, 2, name: "SPOL")
            //        .WithValueField(19, 5, name: "NBREQ") // TODO: # of DMA request - 1
            //        .WithValueField(24, 3, name: "SYNC_ID") // TODO: RM0468: Table120 for DMAMUX1 and Table123 for DMAMUX2 and 5-bits long
            //        .WithReservedBits(27, 5); // TODO: RM0468: DMAMUX2 will have fewer reserved since SYNC_ID field is longer
            //}

            private uint HandleConfigurationRead()
            {
                var returnValue = 0u;

		returnValue |= (uint)(dmaRequestId << 0);
		returnValue |= (uint)(synchronisationId << 24);

		returnValue |= (synchronisationOverrunInterruptEnable ? (1u << 8) : 0u);
		returnValue |= (eventGenerationEnable ? (1u << 9) : 0u);
		returnValue |= (synchronisationEnable ? (1u << 16) : 0u);

		returnValue |= ((uint)synchronisationPolarity << 17);
		returnValue |= ((numberOfDMARequests - 1) << 19);
 
                parent.Log(LogLevel.Debug, "HandleConfigurationRead:[{0}] returning 0x{1:X}", requestNo, returnValue);
                return returnValue;
            }

            private void HandleConfigurationWrite(uint value)
            {
                parent.Log(LogLevel.Debug, "HandleConfigurationWrite:[{0}] value 0x{1:X}", requestNo, value);

		parent.Log(LogLevel.Debug, "HandleConfigurationWrite:[{0}] parent.numberOfRequestTriggerInputs {1}", requestNo, parent.numberOfRequestTriggerInputs);
		parent.Log(LogLevel.Debug, "HandleConfigurationWrite:[{0}] parent.numberOfSynchronisationInputs {1}", requestNo, parent.numberOfSynchronisationInputs);

		// TODO:ASCERTAIN: is the following correct: since RM0468 Figure 85 suggests dmamux_req_inX (Table 118) maps through the SYNC to dmamux_req_outX and dmamux_evtX
		// So dmamux_req_outX is 16 for DMAMUX1 and 8 for DMAMUX2

		// ASCERTAIN: RM0468 Rev3 Table118 DMAMUX1 shows ID
		// 137; which will not fit in a 7-bit field. So we
		// treat both DMAMUX1 and DMAMUX2 as an 8-bit field
		// for now
		//dmaRequestId = (byte)((value >> 0) & 0x7F); // TODO: mask should be 0x1F for DMAMUX2
		dmaRequestId = (byte)((value >> 0) & 0xFF);

		synchronisationId = (byte)((value >> 24) & (parent.numberOfSynchronisationInputs - 1));

		synchronisationOverrunInterruptEnable = (0 != (value & (1 << 8)));
		eventGenerationEnable = (0 != (value & (1 << 9)));
		synchronisationEnable = (0 != (value & (1 << 16)));

		synchronisationPolarity = (SynchronisationPolarity)((value >> 17) & 0x3);
		numberOfDMARequests = ((uint)((value >> 19) & 0x1F) + 1);

		// TODO:
		parent.Log(LogLevel.Debug, "HandleConfigurationWrite:[{0}] numberOfDMARequests {1}", requestNo, numberOfDMARequests);

		// dmaRequestId value implies RX or TX DMA direction
		//
		// We can do something like:
		//   IBusRegistered<IBusPeripheral> whatIsAt;
		//   whatIsAt = sysbus.WhatIsAt(sourceAddress);
		// to get a peripheral object for an address. However we have a logical number to peripheral mapping to resolve.

		// We need to know whether we are DMAMUX1 or DMAMUX2 for the destination // though it should hardwired in the repl since it is fixed
		// requestNo for DMAMUX1 maps to: 0..7 -> DMA1 0..7 and 8..15 -> DMA2 0..7
		// requestNo for DMAMUX2 maps to: 0..7 -> BDMA 0..7

                return;
            }

	    private readonly STM32H7_DMAMUX parent;
            private readonly int requestNo;
	    //private DoubleWordRegisterCollection registers;

	    private byte dmaRequestId;
	    private bool synchronisationOverrunInterruptEnable;
	    private bool eventGenerationEnable;
	    private bool synchronisationEnable;
	    private SynchronisationPolarity synchronisationPolarity;
	    private uint numberOfDMARequests;
	    private byte synchronisationId;

	    private enum SynchronisationPolarity
	    {
		NoEvent = 0,
		RisingEdge = 1,
		FallingEdge = 2,
		BothEdges = 3
	    }

	    private enum Registers
	    {
		Configuration = 0x00, // DMAMUX_CxCR
	    }
	}

        private class RequestGenerator
	{
	    public RequestGenerator(STM32H7_DMAMUX parent, int channelNo)
	    {
		this.parent = parent;
		this.channelNo = channelNo;
	    }

	    public uint Read(long offset)
	    {
		parent.Log(LogLevel.Debug, "STM32H7_DMAMUX:RequestGenerator:Read[{0}] offset 0x{1:X}", channelNo, offset);
		// TODO
		return 0xDEADDEAD;
	    }

            public void Write(long offset, uint value)
            {
                parent.Log(LogLevel.Debug, "STM32H7_DMAMUX:RequestGenerator:Write:[{0}] offset 0x{1:X} value 0x{2:X}", channelNo, offset, value);
		// TODO
	    }

	    public void Reset()
	    {
                parent.Log(LogLevel.Debug, "STM32H7_DMAMUX:RequestGenerator:Reset:[{0}]", channelNo);
		// TODO
	    }

	    private readonly STM32H7_DMAMUX parent;
            private readonly int channelNo;

	    // TODO: Only DMAMUXx_RGyCR register state for the specified channel
	}
    }
}
