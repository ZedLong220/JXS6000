#include <algorithm>
#include <cmath>
#include <fstream>
#include <iomanip>
#include <iostream>
extern "C" {
#define main legacy_checks_main
#include "can42_checks.c"
#undef main
}
#include "buffered_path.h"

struct ArcDrive {
    BufferedPath path;
    float p[2]{}, v[2]{};
    unsigned ticks=0, finishedAt=0;
};
static ArcDrive drives[5];
static unsigned cursor, silentNode;
static const float firstPose[6]={-2.083333f,-20.833333f,-83.333333f,-5.416667f,7.5f,0};
static const double scales[5]={50.0/360,-100.0/360,100.0/360,-30.0/360,-30.0/360};
static float value(const uint8_t *p) { float v;memcpy(&v,p,4);return v; }
static int32_t step(float rev) { return (int32_t)lroundf(rev*51200); }

static void pump(void)
{
    while(cursor<count) {
        Frame f=sent[cursor++];unsigned node=f.id>>7,cmd=f.id&127;
        if(node>=1 && node<=5) {
            auto &d=drives[node-1];
            if(cmd==0x0D && f.len==2)d.path.Clear((uint16_t)(f.data[0]|((uint16_t)f.data[1]<<8)));
            if((cmd==0x0A || cmd==0x0B) && f.len==8) {
                auto p=cmd==0x0A?d.p:d.v;p[0]=value(f.data);p[1]=value(f.data+4);
            }
            if(cmd==0x0C && f.len==8) {
                CHECK(d.path.Append(f.data[4],(uint16_t)(f.data[6]|((uint16_t)f.data[7]<<8)),
                    step(d.p[0]),step(d.p[1]),step(d.v[0]),step(d.v[1]),value(f.data),15*51200.0f,15*51200.0f));
            }
            if(cmd==0x3C && f.len==0 && node!=silentNode) {
                d.path.KeepAlive();
                uint8_t status[8]={4,d.path.state,d.path.count,d.path.index,d.path.error,5,
                    (uint8_t)d.path.session,(uint8_t)(d.path.session>>8)};
                rx((uint16_t)((node<<7)|0x3C),status,8);
            }
            if(cmd==0x23 && f.len==0 && node!=silentNode)
                position((uint8_t)node,d.path.position/51200.0f,d.path.state!=2);
            if(cmd==0x0E || cmd==9)d.path.Clear(d.path.session);
        }
        if(node==0 && cmd==0x66 && f.len==8) {
            CHECK(f.data[3]==0x1F && f.data[4]==FIXED_ARC_SEGMENT_COUNT && f.data[5]==0);
            for(auto &d:drives) {
                CHECK(d.path.Start(f.data[4],(uint16_t)(f.data[6]|((uint16_t)f.data[7]<<8)),false));
                d.ticks=d.finishedAt=0;
            }
        }
        if(node==0 && cmd==0x67 && f.len==8) {
            for(auto &d:drives) {
                CHECK(d.path.FinishTogether(f.data[4],(uint16_t)(f.data[6]|((uint16_t)f.data[7]<<8))));
                d.finishedAt=d.ticks;
            }
        }
    }
}
static void update(void) {CAN_DualAxisUpdate();pump();}
static void advance(unsigned ms,std::ostream *samples=nullptr)
{
    for(unsigned m=0;m<ms;m++) {
        for(auto &d:drives)for(unsigned t=0;t<20;t++) {
            bool active=d.path.Active();d.path.Fill();d.path.Tick();
            if(active){++d.ticks;if(d.path.state==3)d.finishedAt=d.ticks;}
        }
        ++now;update();
        if(samples && drives[0].ticks>=400) {
            *samples<<"["<<std::max(0.0,((double)drives[0].ticks-400)/20000);
            for(unsigned i=0;i<5;i++)*samples<<","<<drives[i].path.position/51200.0/scales[i];
            *samples<<",0],\n";
        }
    }
}
static void start_arc(void)
{
    static const float home[6]={0};
    static const float nearFirst[6]={-2.083333f,-20.833333f,-66.666667f,-5.416667f,7.5f,0};
    reset();silentNode=0;
    control(0x58,nullptr,0);fixed_feedback(home);CAN_DualAxisUpdate();
    fixed_finish(nearFirst);fixed_finish(firstPose);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_VERIFY_POSITION && servoPulse==500);
    for(unsigned i=0;i<5;i++){drives[i]=ArcDrive{};drives[i].path.position=step(firstPose[i]);}
    cursor=count;now+=1000;fixed_feedback(firstPose);pump();
    for(unsigned retry=0;retry<100 && CAN_DualAxisGetState()==DUAL_AXIS_STARTING;retry++)advance(5);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING);
    for(unsigned node=1;node<=5;node++)CHECK(occurrences((uint16_t)((node<<7)|0x0C))==FIXED_ARC_SEGMENT_COUNT);
    CHECK(last((6<<7)|0x0D)==nullptr);
    CHECK(occurrences(0x66)==2); // first timed move, then exactly one arc start
}
static void check_hold(void)
{
    for(unsigned node=1;node<=6;node++) {
        CHECK(occurrences((uint16_t)((node<<7)|0x0E))>0);
        CHECK(occurrences((uint16_t)((node<<7)|9))==0);
        if(node<=5)CHECK(!drives[node-1].path.Active());
    }
}
int main(int argc,char **argv)
{
    CHECK(argc==2);
    start_arc();
    std::ofstream actual(argv[1]);CHECK(actual.good());actual<<std::setprecision(12)<<"[\n";
    unsigned middleTicks=400;
    for(unsigned i=0;i<FIXED_ARC_SEGMENT_COUNT/2;i++)middleTicks+=(unsigned)lroundf(fixedArcPoints[i].duration*1000)*20;
    while(drives[0].ticks<middleTicks)advance(1,&actual);
    CHECK(drives[0].path.Active() && std::abs(drives[0].path.velocity)>51200);
    const double expectedMiddle[5]={-50.28666400864631,90,-280,30,-90};
    for(unsigned i=0;i<5;i++)CHECK(std::abs(drives[i].path.position/51200.0/scales[i]-expectedMiddle[i])<.0003);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING && servoPulse==500);
    while(drives[0].path.Active())advance(1,&actual);
    // The five encoders are polled in separate 10 ms slots. The gripper may
    // open only after a complete fresh, finished snapshot of all five axes.
    for(unsigned settle=0;settle<100 && servoPulse!=1500;settle++)advance(1);
    actual<<"null]\n";actual.close();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_VERIFY_POSITION && CAN_DualAxisIsActive());
    CHECK(servoPulse==1500 && occurrences(0x67)==1);
    CHECK(occurrences(0x66)==2);
    for(unsigned i=0;i<5;i++) {
        CHECK(drives[i].path.state==3 && drives[i].finishedAt==drives[0].finishedAt);
        CHECK(drives[i].path.velocity==0);
        CHECK(drives[i].path.position==step(i==4?7.5f:fixedArcPoints[FIXED_ARC_SEGMENT_COUNT-1].target[i]));
        CHECK(occurrences((uint16_t)(((i+1)<<7)|0x0C))==FIXED_ARC_SEGMENT_COUNT);
    }
    unsigned commonFinish=drives[0].finishedAt;
    CAN_DualAxisStop();pump();check_hold();

    start_arc();advance(300);control(9,nullptr,0);pump();check_hold();
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_IDLE && !CAN_DualAxisIsActive());
    advance(1100);CHECK(occurrences(0x66)==2); // no delayed continuation after stop

    start_arc();advance(200);drives[3].path.Fail(BufferedPath::ErrorMotorStalled);advance(50);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==5);
    CHECK(last(0x38)->data[4]==4 && last(0x38)->data[5]==BufferedPath::ErrorMotorStalled);check_hold();

    start_arc();advance(200);silentNode=4;advance(800);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==2);check_hold();

    start_arc();failServo=1;advance((unsigned)(FIXED_ARC_DURATION_SEC*1000)+200);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==8);check_hold();
    unsigned syncsAfterServoFault=occurrences(0x66);
    advance(1100);CHECK(occurrences(0x66)==syncsAfterServoFault);
    std::cout<<"FIXED ARC MASTER -> FIVE REAL BUFFERED EXECUTORS PASSED: "<<checks
        <<"; common finish tick="<<commonFinish<<"; middle passes without stop; fault/offline/stop hold verified\n";
}
