#include "../Ctrl/Motor/buffered_path.h"
#include <cassert>
#include <fstream>
#include <iostream>
#include <algorithm>
#include <cstdlib>
static unsigned checks;
#define CHECK(x) do { ++checks; if(!(x)) { std::cerr<<"FAIL "<<__LINE__<<": "<<#x<<"\n"; std::exit(2); } } while(0)
static void run(BufferedPath &p,unsigned ticks) {
    for(unsigned t=0;t<ticks;++t) { if(t%100==0)p.KeepAlive(); p.Fill(); p.Tick(); }
}
int main(int argc,char **argv) {
    BufferedPath p;
    p.Clear(10);
    CHECK(p.Append(0,10,0,10000,0,1000,1,30000,100000));
    CHECK(p.Append(1,10,10000,20000,1000,0,1,30000,100000));
    CHECK(p.Start(2,10,false));
    run(p,400+20000);
    CHECK(p.state==2 && p.position==10000 && p.velocity==1000);
    run(p,20000);
    CHECK(p.state==3 && p.position==20000 && p.velocity==0);
    p.Clear(11); CHECK(!p.Append(1,11,0,1,0,0,1,1000,1000));
    CHECK(!p.Append(0,12,0,1,0,0,1,1000,1000));
    CHECK(!p.Append(0,11,0,100000,0,0,.05f,1000,1000)); CHECK(p.state==4);
    p.Clear(12); CHECK(p.Append(0,12,0,-10000,0,0,1,30000,100000));
    CHECK(p.Start(1,12,false)); run(p,1400); CHECK(p.position<0);
    for(unsigned t=0;t<10001;++t) {p.Fill();p.Tick();} CHECK(p.state==4 && p.error==1);
    p.Clear(13); CHECK(p.Append(0,13,0,10000,0,1000,1,30000,100000));
    CHECK(p.Append(1,13,10000,0,1000,0,1,30000,100000));
    CHECK(p.Start(2,13,true)); run(p,400+20000*6); CHECK(p.state==2 && p.position==0);
    p.Clear(16); CHECK(p.Append(0,16,0,10000,1000,1000,1,30000,100000));
    CHECK(p.Append(1,16,10000,0,1000,1000,1,30000,100000)); CHECK(p.Start(2,16,true));
    CHECK(p.velocity==0); run(p,400+40000*3); CHECK(p.state==2 && p.position==0 && p.velocity==1000);
    p.Clear(14); CHECK(p.count==0 && !p.Active());
    for(unsigned i=0;i<32;++i) CHECK(p.Append((uint8_t)i,14,i,i+1,0,0,1,1000,1000));
    CHECK(!p.Append(32,14,32,33,0,0,1,1000,1000));
    p.Clear(15); CHECK(p.Append(0,15,0,10,0,0,1,1000,1000)); CHECK(p.Start(1,15,false));
    for(unsigned t=0;t<401;++t)p.Tick(); CHECK(p.state==4 && p.error==4); // producer starvation
    if(argc>1) {
        std::ifstream input(argv[1]); unsigned n,repeat; float start[2]; input>>n>>repeat>>start[0]>>start[1]; CHECK(input.good());
        BufferedPath axes[2]; for(auto &axis:axes)axis.Clear(21);
        float previous[2]={start[0],start[1]},v0[2]={0,0}; unsigned totalTicks=400;
        for(unsigned i=0;i<n;++i) {
            float t,end[2],v1[2];input>>t>>end[0]>>end[1]>>v1[0]>>v1[1]; CHECK(input.good());
            for(unsigned j=0;j<2;++j) {
                CHECK(axes[j].Append((uint8_t)i,21,(int32_t)lroundf(previous[j]*51200),(int32_t)lroundf(end[j]*51200),
                    (int32_t)lroundf(v0[j]*51200),(int32_t)lroundf(v1[j]*51200),t,4.2f*51200,6.25f*51200));
                previous[j]=end[j];v0[j]=v1[j];
            }
            totalTicks+=(unsigned)lroundf(t*1000)*20;
        }
        double weights[2],maximumError[2]={0,0}; input>>weights[0]>>weights[1]; CHECK(!input.fail());
        for(unsigned axis=0;axis<2;++axis) for(unsigned i=0;i<n;++i) {
            const auto &b=axes[axis].blocks[i];
            double t=b.ticks/20000.0;
            for(unsigned k=0;k<=1000;++k) {
                unsigned tick=b.ticks*k/1000;
                double s=(double)tick/b.ticks;
                double row[6]={(double)b.p0,b.p0+b.v0*t/5,b.p0+2*b.v0*t/5,
                    b.p1-2*b.v1*t/5,b.p1-b.v1*t/5,(double)b.p1};
                for(unsigned degree=5;degree>0;--degree)for(unsigned j=0;j<degree;++j) row[j]=row[j]*(1-s)+row[j+1]*s;
                int32_t actual,velocity; BufferedPath::Evaluate(b,tick,actual,velocity);
                maximumError[axis]=std::max(maximumError[axis],std::abs(actual-row[0]));
            }
        }
        // Include integer rounding and the 1 ms straight sample interval's
        // a*h*h/8 error. Combine axes using the URDF maximum lever arms.
        double numericMm=0;
        for(unsigned i=0;i<2;++i)numericMm+=weights[i]*(maximumError[i]+2+6.25*51200*.001*.001/8);
        CHECK(numericMm<.1);
        std::cout<<"Sampled drive float TCP error plus interpolation allowance: "<<numericMm<<" mm\n";
        CHECK(axes[0].Start(n,21,false) && axes[1].Start(n,21,false));
        for(unsigned tick=0;tick<totalTicks;++tick) {
            for(auto &axis:axes) { if(tick%100==0)axis.KeepAlive(); axis.Fill(); axis.Tick(); }
            CHECK(axes[0].state==axes[1].state && axes[0].index==axes[1].index);
        }
        CHECK(axes[0].state==3 && axes[1].state==3);
        CHECK(axes[0].position==(int32_t)lroundf(previous[0]*51200) && axes[1].position==(int32_t)lroundf(previous[1]*51200));
        std::cout<<"HMI wire trajectory completed on both simulated drives in "<<totalTicks<<" ticks\n";
    }
    std::cout<<"BUFFERED PATH CHECKS PASSED: "<<checks<<"\n";
}
