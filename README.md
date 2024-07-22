# renode_devstm32
renode developpement

Use Renode for stm32 emulations , live debug, tests ... : 

Add files for emulation of stm32s (ARMCC /armclang (keil) / armgcc ):

-> stm32g474 
-> stm32g0b1
-> stm32h7b0,stm32h750 with ltdc

You need 2 main files :
- a .resc file which describe the board that you use, components (ucs,leds, interconnections with buses ) , 
which files (hex , elf ) are you using on your uc...

- a .repl file that you call in your .resc file


Steps:

1) get renode last release:
https://github.com/renode/renode

2) Develop .repl script ( uc  description (peripheral and core) )
   most of cores are already defined in renode ( cortex m0/+ , m4 ,m7..)
   A lot of peripherals like  gpios , integrated memories , nvic,exti , ustarts ,spis are already developped in renode.
   you just have to call them in your stmxxx.repl file

  Ex : 
  cpu: CPU.CortexM @ sysbus
    cpuType: "cortex-m7"
    numberOfMPURegions: 16
    nvic: nvic


  Some peripharals are differents for each uc and you need to implement them 
  
  Ex : 
  the stm32xxx RCC registers.

  for the stm32 rcc you must develop a Cs (C# file) with a class stm32xxx_rcc and you can call it directly on your  stmxxx.rep file

in your stm32h7b0_rcc.cs file :
![image](https://github.com/user-attachments/assets/b69768b9-6db5-46e1-bb7b-dbe0c8c65448)

in your stm32h7b0.repl :

rcc: Miscellaneous.STM32H7B0_RCC @ sysbus 0x58024400
  rtcPeripheral :true
