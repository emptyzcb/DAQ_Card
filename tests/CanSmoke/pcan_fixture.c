typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
#define API __declspec(dllexport) __stdcall
struct msg { u32 id; u8 flags; u8 len; u8 data[8]; };
struct stamp { u32 ms; u16 overflow; u16 micros; };
_Static_assert(sizeof(struct msg)==16 && sizeof(struct stamp)==8, "wrong layout");
static int opened, read_once, inject_status;
static u32 listen;
static struct msg last;
u32 API CAN_Initialize(u16 ch,u16 baud,u8 a,u32 b,u16 c)
{ if(ch!=0x51) return 0x1400; opened=1; read_once=0; if(baud==0x011c) return 0x2000000; if(baud!=0x001c) return 0x8000; return 0; }
u32 API CAN_Uninitialize(u16 ch) { if(ch!=0x51) return 0x1400; opened=0; listen=0; return 0; }
u32 API CAN_Read(u16 ch,struct msg *m,struct stamp *t)
{ if(!opened || ch!=0x51) return 0x4000000; if(inject_status) { inject_status=0; m->id=0; m->flags=0x80; m->len=4; for(int i=0;i<8;i++) m->data[i]=0; m->data[3]=4; t->ms=1235; t->overflow=0; t->micros=0; return 4; } if(read_once++) return 0x20; m->id=0x123; m->flags=0; m->len=3; for(int i=0;i<8;i++) m->data[i]=0; m->data[0]=1; m->data[1]=0xa5; m->data[2]=0xff; t->ms=1234; t->overflow=0; t->micros=567; return 0; }
u32 API CAN_Write(u16 ch,struct msg *m) { if(!opened || ch!=0x51) return 0x4000000; last=*m; return 0; }
u32 API CAN_GetValue(u16 ch,u8 param,u32 *value,u32 size) { if(size!=4) return 0x4000; if(param==8) { *value=listen; return 0; } if(param!=0x0d) return 0x4000; *value=ch==0x51?1:0; return 0; }
u32 API CAN_SetValue(u16 ch,u8 param,u32 *value,u32 size) { if(ch!=0x51 || param!=8 || size!=4) return 0x4000; listen=*value; return 0; }
u32 API CAN_GetStatus(u16 ch) { return opened ? 0 : 0x4000000; }
u32 API CAN_GetErrorText(u32 code,u16 language,char* text) { const char s[]="fixture error"; for(int i=0;i<sizeof(s);i++) text[i]=s[i]; return 0; }
u32 API Fixture_InjectStatus(void) { inject_status=1; return 0; }
u32 API Fixture_IsOpen(void) { return opened; }
u32 API Fixture_LastId(void) { return last.id; }
u32 API Fixture_LastFlags(void) { return last.flags; }
u32 API Fixture_LastDlc(void) { return last.len; }
u32 API Fixture_LastByte(u32 n) { return n<8?last.data[n]:256; }
