//
// Copyright (c) 2010-2024 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Linq;
using System.Collections.Generic;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Core;
using Antmicro.Renode.Utilities;
using Antmicro.Renode.Peripherals.Memory;

namespace Antmicro.Renode.Peripherals.I2C
{
    public class I2C_EEPROM : II2CPeripheral
    {
        public I2C_EEPROM(MappedMemory underlyingMemory)
        {
            Reset();

            this.underlyingMemory = underlyingMemory;
            underlyingMemory.ResetByte = EmptySegment;
        }

        public void Reset()
        {

        }

        public void Write(byte[] data)
        {
            if(data.Length == 0)
            {
               return;
            }

            if(data.Length <= 2 && command == false )
            {
                
                if( data.Length == 2 )
                {
                    offset = ( data[0]&0xFF) << 8 | ( data[1]&0xFF) ;
                    this.Log(LogLevel.Warning, "offset = {0} 0x{1} 0x{2}",offset,data[0],data[1] );
                }
                else
                {
                   // offset = data[0]&0xFF;
                    this.Log(LogLevel.Warning, "offset = {0} 0x{1} ",offset,data[0] );
                }
                
                command = true;
                //offset += data.Length;
            }else
            {
                underlyingMemory.WriteBytes( offset, data );
                this.Log(LogLevel.Noisy, "Write {0}", data.Select(x => x.ToString("X")).Aggregate((x, y) => x + " " + y));
            }
        }

        public MappedMemory UnderlyingMemory => underlyingMemory;

        public byte[] Read(int count = 0)
        {
            if(count == 0)
            {
                byte[] empty = new byte[0];
                return empty;
            }

            byte[] buf = underlyingMemory.ReadBytes(offset,count);
            //offset += buf.Length;
            this.Log(LogLevel.Noisy, "Read {0}", buf.Select(x => x.ToString("X")).Aggregate((x, y) => x + " " + y));
            
            return buf;
        }

        //we are required to implement this method, but in case of this device there is nothing we want to do here
        public void FinishTransmission()
        {
            command = false;
        }

        public uint SerialNumber { get; set; }



        private byte[] message;
        readonly private CRCEngine crc;
        private readonly MappedMemory underlyingMemory;

        private int offset = 0;

         private bool command = false;

        private const byte EmptySegment = 0xff;
    }
}
