#include "can_protocol.h"
#include "serial_cmd.h"
#include "host_bridge.h"
#include "gripper_servo.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { uint16_t id; uint8_t len, data[8]; } Frame;
static FakeCan registers;
CAN_HandleTypeDef hcan1 = { &registers };
UART_HandleTypeDef huart1, huart2;
static uint32_t now;
static Frame sent[32768];
static unsigned count, checks;
static int failId = -1;
static int failOnceId = -1;
static uint16_t servoPulse;
static int failServo;
void GripperHardware_Init(void) { servoPulse=0U; failServo=0; }
int GripperHardware_WritePulse(uint16_t pulse)
{
    if (failServo) return -1;
    servoPulse=pulse; return 0;
}
static char uartText[8192];
static size_t uartLength;
#define CHECK(x) do { ++checks; if (!(x)) { fprintf(stderr,"FAIL %d: %s\n",__LINE__,#x); exit(2); } } while (0)

uint32_t HAL_GetTick(void) { return now; }
uint32_t HAL_CAN_GetTxMailboxesFreeLevel(CAN_HandleTypeDef *h) { (void)h; return 3; }
uint32_t HAL_CAN_IsTxMessagePending(CAN_HandleTypeDef *h,uint32_t m) { (void)h;(void)m;return 0; }
HAL_StatusTypeDef HAL_CAN_AddTxMessage(CAN_HandleTypeDef *h,CAN_TxHeaderTypeDef *tx,uint8_t *data,uint32_t *mailbox)
{
    if ((int)tx->StdId == failOnceId) { failOnceId=-1; return HAL_ERROR; }
    if ((int)tx->StdId == failId) return HAL_ERROR;
    CHECK(count < sizeof(sent)/sizeof(sent[0]));
    sent[count].id = (uint16_t)tx->StdId;
    sent[count].len = (uint8_t)tx->DLC;
    memcpy(sent[count++].data,data,tx->DLC);
    *mailbox = CAN_TX_MAILBOX0;
    h->Instance->TSR = CAN_TSR_TXOK0;
    return HAL_OK;
}
HAL_StatusTypeDef HAL_CAN_AbortTxRequest(CAN_HandleTypeDef *h,uint32_t m) {(void)h;(void)m;return HAL_OK;}
HAL_StatusTypeDef HAL_CAN_ConfigFilter(CAN_HandleTypeDef *h,CAN_FilterTypeDef *f){(void)h;(void)f;return HAL_OK;}
HAL_StatusTypeDef HAL_CAN_Start(CAN_HandleTypeDef *h){(void)h;return HAL_OK;}
HAL_StatusTypeDef HAL_CAN_ActivateNotification(CAN_HandleTypeDef *h,uint32_t n){(void)h;(void)n;return HAL_OK;}
HAL_StatusTypeDef HAL_UART_Transmit(UART_HandleTypeDef *h,uint8_t *p,uint16_t n,uint32_t t)
{
    (void)h;(void)t;
    CHECK(uartLength+n < sizeof(uartText));
    memcpy(uartText+uartLength,p,n); uartLength+=n; uartText[uartLength]=0;
    return HAL_OK;
}
HAL_StatusTypeDef HAL_UART_Receive_IT(UART_HandleTypeDef *h,uint8_t *p,uint16_t n){(void)h;(void)p;(void)n;return HAL_OK;}
HAL_StatusTypeDef HAL_UART_AbortReceive(UART_HandleTypeDef *h){(void)h;return HAL_OK;}
void Error_Handler(void){abort();}

static void reset(void)
{
    now=100; count=uartLength=0; failId=failOnceId=-1;
    CAN_DualAxisInit(); SerialCmd_Init(); Gripper_Init();
}
static unsigned occurrences(uint16_t id)
{
    unsigned n=0; for(unsigned i=0;i<count;++i) if(sent[i].id==id) ++n; return n;
}
static Frame *last(uint16_t id)
{
    for(unsigned i=count;i>0;--i) if(sent[i-1].id==id) return &sent[i-1];
    return NULL;
}
static void rx(uint16_t id,const uint8_t *data,uint8_t len)
{
    CAN_RxHeaderTypeDef hdr; uint8_t bytes[8]={0}; memset(&hdr,0,sizeof(hdr));
    hdr.StdId=id; hdr.DLC=len;
    if(len)memcpy(bytes,data,len);
    unsigned before=count;
    CAN_RxCallback(&hdr,bytes);
    CHECK(count==before); /* no blocking CAN transmit inside RX IRQ */
}
static void position(uint8_t node,float rev,uint8_t finished)
{
    uint8_t bytes[8]={0}; memcpy(bytes,&rev,4); bytes[4]=finished;
    rx((uint16_t)((node<<7)|0x23),bytes,8);
}
static void positions(float a,float b,uint8_t finish)
{
    position(2,a,finish); position(3,b,finish);
}
static void control(uint8_t cmd,const uint8_t *data,uint8_t len)
{
    rx(cmd,data,len); CAN_DualAxisUpdate();
}
static void serial_line(const char *text)
{
    while(*text)SerialCmd_OnRxByte((uint8_t)*text++);
    SerialCmd_OnRxByte('\r');
    SerialCmd_Process();
}
static void goal(uint8_t cmd,float rev,float duration)
{
    uint8_t data[8]; memcpy(data,&rev,4); memcpy(data+4,&duration,4);
    control(cmd,data,8);
}
static void finish(float a,float b)
{
    now+=25; positions(a,b,1); CAN_DualAxisUpdate();
}
static void check_order(void)
{
    unsigned j2=4096,j3=4096,sync=4096;
    for(unsigned i=0;i<count;++i) {
        if(sent[i].id==0x108) j2=i;
        if(sent[i].id==0x188) j3=i;
        if(sent[i].id==0x66) sync=i;
    }
    CHECK(j2<j3 && j3<sync);
    CHECK(sent[sync].len==2 && sent[sync].data[0]==0xFF && sent[sync].data[1]==0x66);
}
static void repeated_moves(void)
{
    reset(); positions(0,0,1); CAN_DualAxisUpdate();
    goal(0x60,-1,1); CHECK(occurrences(0x66)==0);
    goal(0x61,2,1); CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING);
    check_order();
    Frame *ack=last(0x3B);
    CHECK(ack && ack->data[2]==0x61 && ack->data[4]==8 && ack->data[6]==1);
    uint8_t checksum=0x61^8; uint8_t payload[8]; float rev=2,time=1;
    memcpy(payload,&rev,4);memcpy(payload+4,&time,4);
    for(unsigned i=0;i<8;++i)checksum^=payload[i];
    CHECK(ack->data[5]==checksum);
    /* A finish flag at the old position must not finish the new command. */
    finish(0,0); CHECK(CAN_DualAxisIsActive());
    finish(-1,2); CHECK(CAN_DualAxisGetState()==DUAL_AXIS_IDLE);
    CHECK(CAN_DualAxisMove(0,0,1)==0);
    CAN_DualAxisUpdate(); finish(0,0);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_IDLE);
    CHECK(occurrences(0x66)==2);
    for(unsigned i=0;i<count;++i) {
        uint8_t cmd=sent[i].id&0x7F, node=sent[i].id>>7;
        CHECK(cmd!=0x36 && cmd!=0x39); /* no unsupported brake/capability traffic */
        CHECK(node==0 || node==2 || node==3);
    }
}
static void path_and_capacity(void)
{
    uint8_t sync[2]={0xFF,0x66},action;
    reset(); positions(0,0,1); CAN_DualAxisUpdate();
    action=0; control(0x3A,&action,1);
    goal(0x62,1,1); goal(0x63,-1,1); control(0x66,sync,2);
    CHECK(CAN_TrajectoryCount()==1 && occurrences(0x66)==0);
    goal(0x62,0,1); goal(0x63,0,1); control(0x66,sync,2);
    action=1; control(0x3A,&action,1);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING);
    finish(1,-1); CAN_DualAxisUpdate(); finish(0,0);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_IDLE && occurrences(0x66)==2);
    CAN_TrajectoryClear();
    for(unsigned i=0;i<32;++i) CHECK(CAN_TrajectoryEnqueue(0,0,1)==0);
    CHECK(CAN_TrajectoryEnqueue(0,0,1)!=0);
    CAN_TrajectoryClear();
    CHECK(CAN_TrajectoryEnqueue(1,1,1)==0);
    CHECK(CAN_TrajectoryStart(1)==0);
    positions(0,0,1); CAN_DualAxisUpdate(); finish(1,1); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisIsActive());
    CAN_DualAxisStop();
    CHECK(!CAN_DualAxisIsActive());
    for(uint8_t node=1;node<=6;++node) {
        CHECK(occurrences((uint16_t)((node<<7)|0x0E))==1);
        CHECK(occurrences((uint16_t)((node<<7)|0x09))==0);
    }
}
static void failures(void)
{
    reset();
    CHECK(CAN_DualAxisMove(NAN,0,1)!=0);
    CHECK(CAN_DualAxisMove(0,0,INFINITY)!=0);
    CHECK(CAN_DualAxisMove(40001,0,1)!=0);
    CHECK(CAN_DualAxisMove(0,0,0)!=0);
    CHECK(CAN_DualAxisMove(0,0,121)!=0);
    CHECK(CAN_DualAxisMove(1,1,1)==0);
    position(2,0,1); CAN_DualAxisUpdate();
    now+=1501; CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==2);
    CHECK(occurrences(0x66)==0);
    reset(); positions(0,0,1); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisMove(1,1,1)==0); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisMove(2,2,1)!=0);
    now+=751; CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT);
    CHECK(occurrences(0x10E)==1 && occurrences(0x18E)==1);
    CHECK(occurrences(0x109)==0 && occurrences(0x189)==0);
    reset(); positions(0,0,1); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisMove(1,1,1)==0); CAN_DualAxisUpdate();
    now+=6001; positions(0,0,1); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==3);
    reset(); positions(0,0,1); CAN_DualAxisUpdate();
    failId=0x188; CHECK(CAN_DualAxisMove(1,1,1)==0); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && occurrences(0x66)==0);
}
static void transport(void)
{
    uint8_t action=0;
    reset();
    /* Query frames get an ACK but do not make an axis online or get retransmitted. */
    rx(0x123,NULL,0); CAN_DualAxisUpdate();
    CHECK(last(0x3B)->data[6]==1 && last(0x38)==NULL && occurrences(0x123)==0);
    now=600; CAN_DualAxisUpdate(); CHECK(last(0x38)->data[2]==0);
    /* Expired half pairs cannot be combined with a new goal. */
    goal(0x60,1,1); now+=1501; goal(0x61,1,1);
    CHECK(!CAN_DualAxisIsActive());
    control(0x3A,&action,1);
    const char *text="O\rt0608000080BF0000803F\rt0618000000400000803F\r";
    while(*text)SerialCmd_OnRxByte((uint8_t)*text++);
    positions(0,0,1); CAN_DualAxisUpdate(); SerialCmd_Process(); CAN_DualAxisUpdate(); HostBridge_Process();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING);
    CHECK(strstr(uartText,"t03B8")!=NULL);
    CAN_DualAxisStop(); HostBridge_Process();
    reset(); control(0x59,NULL,0);
    CHECK(last(0x3B)->data[6]==1);
    for(uint8_t node=1;node<=6;++node)
        CHECK(occurrences((uint16_t)((node<<7)|0x09))==1);
    reset();
    for(unsigned i=0;i<140;++i)position(2,0,1);
    CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT);
}
static void spline_status(uint8_t node,uint8_t st,uint8_t loaded,uint16_t session,uint8_t error)
{
    uint8_t data[8]={4,st,loaded,0,error,5,(uint8_t)session,(uint8_t)(session>>8)};
    rx((uint16_t)((node<<7)|0x3C),data,8);
}
static uint16_t reset_session(void)
{
    Frame *f=last(0x10D);
    CHECK(f!=NULL && f->len==2);
    return (uint16_t)(f->data[0]|((uint16_t)f->data[1]<<8));
}
static void spline_load(void)
{
    uint8_t action=0,sync[2]={0xFF,0x66};
    control(0x3A,&action,1); goal(0x68,0,0);
    goal(0x62,1,2); goal(0x63,-1,2); goal(0x64,.1f,-.1f); control(0x66,sync,2);
    CHECK(last(0x3B)->data[6]==1);
    goal(0x62,0,2); goal(0x63,0,2);
    control(0x66,sync,2); CHECK(last(0x3B)->data[6]==0); /* missing velocity must NAK */
    goal(0x62,0,2); goal(0x63,0,2); goal(0x64,0,0); control(0x66,sync,2);
    action=1; control(0x3A,&action,1);
    CHECK(occurrences(0x66)==0);
}
static void continuous_path(void)
{
    uint16_t session;
    reset(); positions(0,0,1); CAN_DualAxisUpdate(); spline_load(); session=reset_session();
    /* Wrong session / only one participating node cannot start a path. */
    spline_status(2,0,0,session,0); spline_status(3,0,0,(uint16_t)(session+1),0); CAN_DualAxisUpdate();
    CHECK(occurrences(0x10C)==0 && occurrences(0x66)==0);
    spline_status(3,0,0,session,0); CAN_DualAxisUpdate();
    CHECK(occurrences(0x10C)==1 && occurrences(0x18C)==1);
    CHECK(last(0x10A)->data[0]==0 && last(0x10C)->data[4]==0);
    { float p0,p1,v0,v1;
      memcpy(&p0,last(0x10A)->data,4); memcpy(&p1,last(0x10A)->data+4,4);
      memcpy(&v0,last(0x10B)->data,4); memcpy(&v1,last(0x10B)->data+4,4);
      CHECK(p0==0 && p1==1 && v0==0 && fabsf(v1-.1f)<1e-7f); }
    spline_status(2,1,1,session,0); spline_status(3,1,1,session,0); CAN_DualAxisUpdate();
    CHECK(occurrences(0x10C)==2 && last(0x10C)->data[4]==1);
    { float p0,p1,v0,v1;
      memcpy(&p0,last(0x10A)->data,4); memcpy(&p1,last(0x10A)->data+4,4);
      memcpy(&v0,last(0x10B)->data,4); memcpy(&v1,last(0x10B)->data+4,4);
      CHECK(p0==1 && p1==0 && fabsf(v0-.1f)<1e-7f && v1==0); }
    spline_status(2,1,2,session,0); CAN_DualAxisUpdate(); CHECK(occurrences(0x66)==0);
    spline_status(3,1,2,session,0); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING && occurrences(0x66)==1);
    CHECK(last(0x66)->len==8 && last(0x66)->data[2]==4 && last(0x66)->data[3]==6 && last(0x66)->data[4]==2);
    unsigned commits=occurrences(0x10C);
    /* A delayed J3 position reply must not stop a healthy local spline. Its
       fresh path heartbeat is the authoritative running liveness signal. */
    now+=800; position(2,.4f,0); spline_status(2,2,2,session,0); spline_status(3,2,2,session,0); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisIsActive() && (last(0x38)->data[2]&6U)==6U);
    now+=1200; positions(1,-1,0); spline_status(2,2,2,session,0); spline_status(3,2,2,session,0); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisIsActive() && occurrences(0x10C)==commits && occurrences(0x66)==1);
    now+=2019; positions(0,0,0); spline_status(2,2,2,session,0); spline_status(3,2,2,session,0); CAN_DualAxisUpdate();
    CHECK(occurrences(0x67)==1 && last(0x67)->len==8 && last(0x67)->data[3]==6);
    now+=31; positions(0,0,1); spline_status(2,3,2,session,0); spline_status(3,3,2,session,0); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_IDLE);
    reset(); positions(0,0,1); CAN_DualAxisUpdate(); spline_load();
    now+=1501; positions(0,0,1); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==4 && occurrences(0x66)==0);
    reset(); positions(0,0,1); CAN_DualAxisUpdate(); spline_load(); session=reset_session();
    spline_status(2,4,0,session,3); spline_status(3,0,0,session,0); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==5 && occurrences(0x66)==0);
    reset(); positions(1,0,1); CAN_DualAxisUpdate(); spline_load();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==6);
}
static void five_host_start(uint8_t mask)
{
    uint8_t begin[2]={4,mask}; control(0x50,begin,2);
    CHECK(last(0x3B)->data[6]==1);
    for(uint8_t node=1;node<=6;++node)if(mask&(1U<<(node-1))) {
        uint8_t p[5]={node,0,0,0,0}; control(0x51,p,5); CHECK(last(0x3B)->data[6]==1);
    }
}
static void five_stage(uint16_t seq,uint8_t mask,float duration,uint8_t missing)
{
    uint8_t d[8]={0}; memcpy(d,&duration,4); d[4]=(uint8_t)seq; d[5]=(uint8_t)(seq>>8);
    control(0x52,d,6); CHECK(last(0x3B)->data[6]==1);
    for(uint8_t node=1;node<=6;++node)if(mask&(1U<<(node-1)))for(uint8_t kind=0;kind<=1;++kind) {
        float value=kind?0.0f:(float)node/10.0f;
        if(node==missing)continue;
        d[0]=node; d[1]=kind; d[2]=(uint8_t)seq; d[3]=(uint8_t)(seq>>8); memcpy(d+4,&value,4);
        control(0x53,d,8); CHECK(last(0x3B)->data[6]==1);
    }
}
static void five_axis_protocol(void)
{
    uint8_t d[8]={0},action=1;
    reset(); d[0]=4; d[1]=0x7F; control(0x50,d,2); CHECK(last(0x3B)->data[6]==0);
    d[0]=d[1]=0;
    five_host_start(63);
    five_stage(0,63,1.0f,6); control(0x54,d,2);
    CHECK(last(0x3B)->data[6]==0 && CAN_TrajectoryCount()==0);
    five_stage(0,63,1.0f,0); now+=5000; control(0x54,d,2);
    CHECK(last(0x3B)->data[6]==0 && CAN_TrajectoryCount()==0);
    five_stage(0,63,1.0f,0); d[0]=1; control(0x54,d,2);
    CHECK(last(0x3B)->data[6]==0 && CAN_TrajectoryCount()==0); d[0]=0;
    five_stage(0,63,1.0f,0); control(0x3A,&action,1);
    CHECK(last(0x3B)->data[6]==0 && !CAN_DualAxisIsActive()); /* uncommitted block cannot start */
    control(0x54,d,2); CHECK(CAN_TrajectoryCount()==1);
    for(uint8_t node=1;node<=5;++node)position(node,0,1);
    CAN_DualAxisUpdate(); control(0x3A,&action,1);
    now+=1501; for(uint8_t node=1;node<=5;++node)position(node,0,1); CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && occurrences(0x66)==0);
    for(uint8_t node=1;node<=6;++node) {
        CHECK(occurrences((uint16_t)((node<<7)|0x0E))>0);
        CHECK(occurrences((uint16_t)((node<<7)|0x09))==0);
    }
    reset(); five_host_start(63);
    for(unsigned i=0;i<32;++i) { five_stage((uint16_t)i,63,1.0f,0); d[0]=(uint8_t)i; control(0x54,d,2); CHECK(CAN_TrajectoryCount()==i+1); }
    CHECK(CAN_MultiStage(32,1.0f)!=0);
    CHECK(CAN_DualAxisSetZero()!=0); /* never cancel an active buffered path to zero */
    for(uint8_t node=1;node<=6;++node)CHECK(occurrences((uint16_t)((node<<7)|0x15))==0);
    for(unsigned i=0;i<count;++i)CHECK((sent[i].id>>7)!=7);

    reset();
    for(uint8_t node=1;node<=5;++node)position(node,0,1);
    CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisSetZero()==0);
    for(uint8_t node=1;node<=5;++node)
        CHECK(occurrences((uint16_t)((node<<7)|0x15))==1);
    CHECK(occurrences((uint16_t)((6U<<7)|0x15))==0); /* offline J6 is not addressed */

    reset(); five_host_start(7); five_stage(0,7,1.0f,0);
    serial_line("O");
    serial_line("t05420000");
    CHECK(CAN_TrajectoryCount()==1 && last(0x3B)->data[6]==1);
    serial_line("t05420000");
    CHECK(CAN_TrajectoryCount()==1 && last(0x3B)->data[6]==1);
    now+=5001; serial_line("t05420000");
    CHECK(CAN_TrajectoryCount()==1 && last(0x3B)->data[6]==0);

    /* J4 may move slightly while the HMI checks the planned path. Rebase a
       sub-0.5-degree drift to the latest encoder sample before block upload. */
    reset(); five_host_start(8); five_stage(0,8,1.0f,0); d[0]=0; d[1]=0;
    control(0x54,d,2); CHECK(CAN_TrajectoryCount()==1);
    position(4,0.03f,1); action=1; control(0x3A,&action,1);
    {
        Frame *resetFrame=last(0x20D); float p0;
        CHECK(resetFrame!=NULL && resetFrame->len==2);
        uint16_t rebaseSession=(uint16_t)(resetFrame->data[0]|((uint16_t)resetFrame->data[1]<<8));
        spline_status(4,0,0,rebaseSession,0); CAN_DualAxisUpdate();
        memcpy(&p0,last(0x20A)->data,4);
        CHECK(fabsf(p0-0.03f)<0.00001f);
    }
}
static void gripper_commands(void)
{
    float angle;
    uint8_t bytes[8];
    reset(); Gripper_GetStatus(bytes);
    CHECK(servoPulse==0 && bytes[6]==0 && bytes[7]==1);
    for(unsigned i=0;i<3;++i) {
        angle=(float)i*90.0f;memcpy(bytes,&angle,4);
        control(0x55,bytes,4);
        CHECK(last(0x3B)->data[6]==1 && servoPulse==500U+i*1000U);
        CHECK(last(0x57)->data[6]==1);
    }
    for(unsigned i=0;i<4;++i) {
        angle=i==0?-1.0f:i==1?181.0f:i==2?NAN:INFINITY;
        memcpy(bytes,&angle,4);control(0x55,bytes,4);
        CHECK(last(0x3B)->data[6]==0 && servoPulse==2500U);
    }
    control(0x55,bytes,3);CHECK(last(0x3B)->data[6]==0);
    control(0x57,NULL,0);CHECK(last(0x3B)->data[6]==1 && last(0x57)->len==8);
    failServo=1;angle=90;memcpy(bytes,&angle,4);control(0x55,bytes,4);
    CHECK(last(0x3B)->data[6]==0 && servoPulse==2500U);
    failServo=0;control(0x56,NULL,0);
    CHECK(last(0x3B)->data[6]==1 && servoPulse==0 && last(0x57)->data[6]==0);
    reset();{uint8_t old[2]={3,63};control(0x50,old,2);CHECK(last(0x3B)->data[6]==0);}
}

static void fixed_feedback(const float pose[6])
{
    static const uint8_t nodes[]={1,2,3,4,5};
    for(unsigned i=0;i<sizeof(nodes);++i)position(nodes[i],pose[nodes[i]-1],1);
    CAN_DualAxisUpdate();
}

static void fixed_check_target(const float expected[6])
{
    float target;
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING);
    static const uint8_t nodes[]={1,2,3,4,5};
    for(unsigned i=0;i<sizeof(nodes);++i) {
        uint8_t node=nodes[i];
        Frame *f=last((uint16_t)((node<<7)|0x08));
        CHECK(f!=NULL && f->len==8);
        memcpy(&target,f->data,4);
        CHECK(fabsf(target-expected[node-1])<0.00001f);
    }
    CHECK(last((uint16_t)((6U<<7)|0x08))==NULL);
}

static void fixed_commit(const float current[6],const float expected[6])
{
    fixed_feedback(current);
    fixed_check_target(expected);
}

static void fixed_finish(const float target[6])
{
    now+=25;
    fixed_feedback(target);
}

static void fixed_return_feedback(const float start[6],uint32_t elapsed)
{
    for(uint8_t node=1;node<=5;++node)
        position(node,start[node-1]*(1.0f-(float)elapsed/10000.0f),elapsed>=10000U);
    CAN_DualAxisUpdate();
}

static void staggered_position_queries(void)
{
    static const float home[6]={0};
    unsigned before[5];

    reset();
    control(0x58,NULL,0);
    fixed_feedback(home);
    CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING);
    for(uint8_t node=1;node<=5;++node)
        before[node-1]=occurrences((uint16_t)((node<<7)|0x23));

    now+=9; CAN_DualAxisUpdate();
    for(uint8_t node=1;node<=5;++node)
        CHECK(occurrences((uint16_t)((node<<7)|0x23))==before[node-1]);

    for(uint8_t expected=1;expected<=5;++expected) {
        now+=(expected==1?1:10);
        CAN_DualAxisUpdate();
        for(uint8_t node=1;node<=5;++node)
            CHECK(occurrences((uint16_t)((node<<7)|0x23))==
                  before[node-1]+(node<=expected?1U:0U));
    }
    CAN_DualAxisStop();
}

static void fixed_pick_sequence(void)
{
    static const float home[6]={0};
    static const float nearFirst[6]={-2.083333f,-19.444444f,-66.666667f,-7.5f,7.5f,0.0f};
    static const float first[6]={-2.083333f,-19.444444f,-83.333333f,-7.5f,7.5f,0.0f};
    static const float second[6]={-6.944444f,-19.444444f,-83.333333f,-7.5f,7.5f,0.0f};
    unsigned beforeThreshold[5], beforeSecond[5];
    float target;
    reset();
    control(0x58,NULL,0);
    CHECK(last(0x3B)->data[6]==1 && CAN_DualAxisIsActive());
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_STARTING);
    CHECK(last(0x38)->data[6]==0x1FU);
    CHECK(occurrences((uint16_t)((4U<<7)|0x23))!=0U);
    fixed_feedback(home);
    CAN_DualAxisUpdate();
    fixed_check_target(first);
    for(uint8_t node=1;node<=5;++node)
        beforeThreshold[node-1]=occurrences((uint16_t)((node<<7)|0x08));
    fixed_finish(nearFirst);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING && servoPulse==1500U);
    CHECK(last(0x57)!=NULL && last(0x57)->data[6]==1U);
    /* Opening the gripper at J3=-240 deg must not restart any axis. */
    for(uint8_t node=1;node<=5;++node)
        CHECK(occurrences((uint16_t)((node<<7)|0x08))==beforeThreshold[node-1]);
    fixed_finish(first);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_VERIFY_POSITION && servoPulse==500U);
    CHECK(last(0x57)!=NULL && last(0x57)->data[6]==1U);
    CHECK(CAN_DualAxisIsActive() && last(0x38)->data[3]==0U);
    CHECK(CAN_FixedPickSequenceStart()!=0 && CAN_DualAxisSetZero()!=0);
    unsigned firstCloseStatus=occurrences(0x57);
    now+=999; fixed_feedback(first);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_VERIFY_POSITION);
    CHECK(occurrences(0x57)==firstCloseStatus); /* close once, keep the wait deadline */
    now+=1; CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_STARTING);
    CHECK(last(0x38)->data[6]==0x01U);
    for(uint8_t node=1;node<=5;++node)
        beforeSecond[node-1]=occurrences((uint16_t)((node<<7)|0x08));
    fixed_feedback(first);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING);
    CHECK(occurrences((uint16_t)((1U<<7)|0x08))==beforeSecond[0]+1U);
    for(uint8_t node=2;node<=5;++node)
        CHECK(occurrences((uint16_t)((node<<7)|0x08))==beforeSecond[node-1]);
    memcpy(&target,last((uint16_t)((1U<<7)|0x08))->data,4);
    CHECK(fabsf(target-second[0])<0.00001f);
    fixed_finish(second);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_VERIFY_POSITION && servoPulse==1500U);
    CHECK(last(0x38)->data[6]==0x1FU);
    now+=999; fixed_feedback(second);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_VERIFY_POSITION);
    now+=1; CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_STARTING && servoPulse==1500U);
    fixed_commit(second,home);
    for(uint8_t node=1;node<=5;++node) {
        float duration;
        memcpy(&duration,last((uint16_t)((node<<7)|0x08))->data+4,4);
        CHECK(duration==10.0f);
    }
    unsigned beforeReturnClose=occurrences(0x57);
    for(uint32_t elapsed=100;elapsed<5000U;elapsed+=100U) {
        now+=100;
        fixed_return_feedback(second,elapsed);
        CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING && servoPulse==1500U);
    }
    now+=99; fixed_return_feedback(second,4999U);
    CHECK(servoPulse==1500U && occurrences(0x57)==beforeReturnClose);
    now+=1; fixed_return_feedback(second,5000U);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING && servoPulse==500U);
    CHECK(occurrences(0x57)==beforeReturnClose+1U);
    for(uint32_t elapsed=5100;elapsed<=10000U;elapsed+=100U) {
        now+=100;
        fixed_return_feedback(second,elapsed);
    }
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_IDLE && !CAN_DualAxisIsActive());
    CHECK(servoPulse==500U && last(0x57)!=NULL && last(0x57)->data[6]==1U);
    CHECK(occurrences(0x57)==beforeReturnClose+1U);

    /* A failed first-pose close must fault and hold every drive, not advance. */
    reset(); control(0x58,NULL,0);
    fixed_feedback(home); CAN_DualAxisUpdate();
    fixed_finish(nearFirst);
    failServo=1; fixed_finish(first);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==8U);
    CHECK(!CAN_DualAxisIsActive());
    for(uint8_t node=1;node<=6;++node) {
        CHECK(occurrences((uint16_t)((node<<7)|0x0E))==1U);
        CHECK(occurrences((uint16_t)((node<<7)|0x09))==0U);
    }

    reset(); control(0x58,NULL,0);
    for(uint8_t node=1;node<=5;++node)
        position(node,node==1?0.021f:0.0f,1);
    CAN_DualAxisUpdate();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==7U);
}

int main(void)
{
    repeated_moves(); path_and_capacity(); failures(); transport();
    continuous_path();
    five_axis_protocol();
    gripper_commands();
    staggered_position_queries();
    fixed_pick_sequence();
    printf("CAN42 CHECKS PASSED: %u\n",checks);
    return 0;
}
