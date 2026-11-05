"""Run the network-configuration C logic on a PC with simulated flash.

This does not build firmware or simulate STM32 flash/electrical behavior.
"""
from pathlib import Path
import subprocess
import tempfile

source = (Path(__file__).resolve().parents[1] / "Application/Src/Task_eth.c").read_text(encoding="utf-8")
body = source[source.index("#define NET_SLOT_A"):source.index("static uint8_t  g_eth_rx_buf")]
body = body.replace("0x08180000UL", "((uintptr_t)slot_a)").replace("0x081A0000UL", "((uintptr_t)slot_b)")
body = body.replace("uint32_t addr, error = 0U;", "uintptr_t addr; uint32_t error = 0U;")
body = body.replace("(uint32_t)&word", "(uintptr_t)&word")
prelude = r'''
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include <assert.h>
#define __ALIGNED(n) __attribute__((aligned(n)))
#define FLASH_TYPEERASE_SECTORS 0
#define FLASH_BANK_2 2
#define FLASH_SECTOR_4 4
#define FLASH_SECTOR_5 5
#define FLASH_VOLTAGE_RANGE_3 3
#define FLASH_TYPEPROGRAM_FLASHWORD 1
#define HAL_OK 0
typedef struct { int TypeErase, Banks, Sector, NbSectors, VoltageRange; } FLASH_EraseInitTypeDef;
typedef struct { uint8_t gateway[4], subnet[4], mac[6], ip[4]; } bsp_w5500_netinfo_t;
static bsp_w5500_netinfo_t g_eth_netinfo = {{192,168,1,1},{255,255,255,0},{2,0,0,0,0,16},{192,168,1,10}};
static uint16_t g_eth_port=5000;
static uint8_t slot_a[32], slot_b[32];
static int erase_fail;
static int HAL_FLASH_Unlock(void) {return 0;}
static int HAL_FLASH_Lock(void) {return 0;}
static int HAL_FLASHEx_Erase(FLASH_EraseInitTypeDef *e,uint32_t *err) { (void)err; if (erase_fail) return 1; memset(e->Sector==4?slot_a:slot_b,255,32); return 0; }
static int HAL_FLASH_Program(int type, uintptr_t dst, uintptr_t src) { (void)type; memcpy((void*)dst,(void*)src,32); return 0; }
static void SCB_InvalidateDCache_by_Addr(void *p,int n) {(void)p;(void)n;}
'''
tests = r'''
int main(void) {
 uint8_t req[23]={0,7,0,0,0,17,1,0x42,2,192,168,1,50,255,255,255,0,192,168,1,1,0x13,0x89}, out[32]={0};
 memset(slot_a,255,32); memset(slot_b,255,32);
 net_load(); assert(g_eth_netinfo.ip[3]==10 && g_eth_port==5000);
 assert(net_handle(req,23,out)==10 && out[9]==0 && net_reset_pending);
 net_load(); assert(g_eth_netinfo.ip[3]==50 && g_eth_port==5001);
 req[12]=51; req[22]=0x8a;
 assert(net_handle(req,23,out)==10 && out[9]==0);
 net_load(); assert(g_eth_netinfo.ip[3]==51 && g_eth_port==5002);
 slot_b[0]^=1; net_load(); assert(g_eth_netinfo.ip[3]==50 && g_eth_port==5001);
 req[12]=0; assert(net_handle(req,23,out)==10 && out[9]!=0);
 req[12]=52; erase_fail=1; assert(net_handle(req,23,out)==10 && out[9]!=0); erase_fail=0;
 net_load(); assert(g_eth_netinfo.ip[3]==50 && g_eth_port==5001);
 req[8]=1; req[5]=3;
 assert(net_handle(req,9,out)==24 && out[5]==18 && out[9]==0 && out[13]==50 && out[22]==0x13 && out[23]==0x89);
 puts("PASS: defaults, TCP read/write, validation, dual-slot fallback, erase failure"); return 0;
}
'''
with tempfile.TemporaryDirectory(prefix="daq_net_check_") as tmp:
    c = Path(tmp) / "test.c"
    exe = Path(tmp) / "test.exe"
    c.write_text(prelude + body + tests, encoding="utf-8")
    subprocess.run(["gcc", "-std=c11", "-Wall", "-Wextra", "-Werror", str(c), "-o", str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
