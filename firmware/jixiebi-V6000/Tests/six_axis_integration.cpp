#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <iostream>
#include <cmath>
#include <algorithm>
extern "C" {
#define main legacy_checks_main
#include "can42_checks.c"
#undef main
}
#include "buffered_path.h"

struct Drive {
    BufferedPath path;
    float p[2]{},v[2]{};
    uint32_t elapsed=0,finishedAt=0;
};
static Drive drives[6];
static unsigned cursor;
static unsigned silentNode;
static float encoderBias[6];
static float get_float(const uint8_t *data) { float v; memcpy(&v,data,4); return v; }
static void pump(void)
{
    while(cursor<count) {
        Frame f=sent[cursor++]; unsigned node=f.id>>7,cmd=f.id&0x7F;
        if(node>=1 && node<=6) {
            auto &d=drives[node-1];
            if(cmd==0x0D && f.len==2) d.path.Clear((uint16_t)(f.data[0]|((uint16_t)f.data[1]<<8)));
            if((cmd==0x0A || cmd==0x0B) && f.len==8) {
                float *value=cmd==0x0A?d.p:d.v;value[0]=get_float(f.data);value[1]=get_float(f.data+4);
            }
            if(cmd==0x0C && f.len==8) {
                auto step=[](float v){return (int32_t)lroundf(v*51200);};
                CHECK(d.path.Append(f.data[4],(uint16_t)(f.data[6]|((uint16_t)f.data[7]<<8)),
                    step(d.p[0]),step(d.p[1]),step(d.v[0]),step(d.v[1]),get_float(f.data),4.2f*51200,6.25f*51200));
            }
            if(cmd==0x3C && f.len==0) {
                if(node==silentNode)continue;
                d.path.KeepAlive();
                uint8_t status[8]={4,d.path.state,d.path.count,d.path.index,d.path.error,5,
                    (uint8_t)d.path.session,(uint8_t)(d.path.session>>8)};
                rx((uint16_t)((node<<7)|0x3C),status,8);
            }
            if(cmd==0x23 && f.len==0 && node!=silentNode)
                position((uint8_t)node,d.path.position/51200.0f+encoderBias[node-1],d.path.state==3);
            if(cmd==9) d.path.Clear(d.path.session);
        }
        if(node==0 && cmd==0x66 && f.len==8) {
            CHECK(f.data[2]==4);
            for(unsigned i=0;i<6;++i)if(f.data[3]&(1U<<i)) {
                CHECK(drives[i].path.Start(f.data[4],(uint16_t)(f.data[6]|((uint16_t)f.data[7]<<8)),f.data[5]!=0));
                drives[i].elapsed=drives[i].finishedAt=0;
            }
        }
    }
}
static void update(void) { CAN_DualAxisUpdate(); pump(); }
static void advance(unsigned milliseconds)
{
    for(unsigned ms=0;ms<milliseconds;++ms) {
        for(unsigned i=0;i<6;++i)for(unsigned tick=0;tick<20;++tick) {
            auto &d=drives[i]; bool active=d.path.Active();
            d.path.Fill();d.path.Tick();
            if(active) { ++d.elapsed; if(d.path.state==3)d.finishedAt=d.elapsed; }
        }
        ++now; if(ms%5==0)update();
    }
    update();
}
static void load_path(const std::vector<Frame> &host,bool repeat=false)
{
    reset(); cursor=silentNode=0;
    for(unsigned i=0;i<6;++i) {
        drives[i].path.Clear(); drives[i].path.position=0;
        encoderBias[i]=0; position((uint8_t)(i+1),0,1);
    }
    update();
    for(auto f:host) {
        if(f.id==0x3A && repeat)f.data[0]=2;
        control((uint8_t)f.id,f.data,f.len);pump();
        CHECK(last(0x3B)->data[6]==1);
    }
    for(unsigned retry=0;retry<200 && CAN_DualAxisGetState()==DUAL_AXIS_STARTING;++retry)advance(5);
    if(CAN_DualAxisGetState()!=DUAL_AXIS_RUNNING)
        std::cerr<<"load failed repeat="<<repeat<<" state="<<CAN_DualAxisGetState()<<" error="<<(unsigned)last(0x38)->data[1]<<"\n";
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING);
    CHECK(last(0x66)->data[3]==63);
}
static void all_stopped(void)
{
    for(unsigned i=0;i<6;++i) {
        CHECK(occurrences((uint16_t)(((i+1)<<7)|9))>0);
        CHECK(!drives[i].path.Active());
    }
}
int main(int argc,char **argv)
{
    CHECK(argc==2);
    std::ifstream input(argv[1]); CHECK(input.good());
    std::vector<Frame> host;
    std::string line;
    while(std::getline(input,line)) {
        unsigned cmd; std::string bytes; std::istringstream row(line); row>>std::hex>>cmd>>bytes;
        CHECK(row.good() || row.eof()); CHECK(bytes.size()%2==0 && bytes.size()<=16);
        Frame f{};f.id=(uint16_t)cmd;f.len=(uint8_t)(bytes.size()/2);
        for(unsigned i=0;i<f.len;++i)f.data[i]=(uint8_t)std::stoul(bytes.substr(i*2,2),nullptr,16);
        host.push_back(f);
    }
    load_path(host);
    unsigned commits[6];for(unsigned i=0;i<6;++i)commits[i]=occurrences((uint16_t)(((i+1)<<7)|0x0C));
    for(unsigned cycle=0;cycle<2000 && CAN_DualAxisIsActive();++cycle)advance(10);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_IDLE);
    for(unsigned i=0;i<6;++i) {
        CHECK(drives[i].path.state==3 && drives[i].finishedAt==drives[0].finishedAt);
        CHECK(occurrences((uint16_t)(((i+1)<<7)|0x0C))==commits[i]);
        CHECK(drives[i].path.position==0);
    }
    CHECK(occurrences(0x66)==1);
    for(unsigned i=0;i<count;++i)CHECK((sent[i].id>>7)!=7);
    unsigned finishTick=drives[0].finishedAt;
    load_path(host,true); advance(finishTick/20*2+100);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_RUNNING && occurrences(0x66)==1);
    for(unsigned i=0;i<6;++i) {
        CHECK(drives[i].path.Active() && drives[i].elapsed==drives[0].elapsed);
        CHECK(drives[i].path.index==drives[0].path.index);
    }
    control(9,nullptr,0);pump();all_stopped();
    load_path(host); advance(100); drives[2].path.Fail(2);advance(50);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==5);all_stopped();
    load_path(host); advance(100);silentNode=6;advance(800);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==2);all_stopped();
    load_path(host); advance(100);encoderBias[3]=.1f;advance(finishTick/20+5100);
    CHECK(CAN_DualAxisGetState()==DUAL_AXIS_FAULT && last(0x38)->data[1]==3);all_stopped();
    std::cout<<"SIX AXIS HMI -> MASTER -> DRIVES PASSED: "<<checks<<"; common finish tick="<<finishTick<<"; repeat/fault/offline/final encoder checks passed\n";
}
