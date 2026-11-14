#include "../Ctrl/Motor/buffered_path.h"
#include <fstream>
#include <iostream>
#include <vector>
#include <string>
#include <cstdlib>
#include <algorithm>

static unsigned checks;
#define CHECK(x) do { ++checks; if(!(x)) { std::cerr<<"FAIL "<<__LINE__<<": "<<#x<<"\n"; std::exit(2); } } while(0)
struct Segment { float seconds; std::vector<float> end, velocity; };

// Run the HMI's quantized one-to-six-axis wire fixture through the actual
// driver executor, including multiple loop seams and synchronized ticks.
int main(int argc,char **argv) {
    CHECK(argc==2 || argc==4);
    float vmax=argc==4?std::stof(argv[2]):4.2f,amax=argc==4?std::stof(argv[3]):6.25f;
    std::ifstream input(argv[1]);
    unsigned axes,count,repeat;
    input>>axes>>count>>repeat;
    CHECK(input.good() && axes>=1 && axes<=6 && count>=1 && count<=32 && repeat<=1);
    std::vector<float> start(axes);
    for(auto &v:start) input>>v;
    std::vector<Segment> segments(count);
    for(auto &s:segments) {
        input>>s.seconds; s.end.resize(axes); s.velocity.resize(axes);
        for(auto &v:s.end) input>>v;
        for(auto &v:s.velocity) input>>v;
    }
    CHECK(!input.fail());
    auto step=[](float v){ return (int32_t)lroundf(v*51200); };
    BufferedPath drives[6];
    unsigned lapTicks=0;
    for(unsigned axis=0;axis<axes;axis++) {
        auto &d=drives[axis]; d.Clear(42);
        float previous=start[axis],velocity=repeat?segments.back().velocity[axis]:0;
        for(unsigned i=0;i<count;i++) {
            auto &s=segments[i];
            CHECK(d.Append((uint8_t)i,42,step(previous),step(s.end[axis]),step(velocity),step(s.velocity[axis]),
                s.seconds,vmax*51200,amax*51200));
            previous=s.end[axis]; velocity=s.velocity[axis];
            if(axis==0) lapTicks+=d.blocks[i].ticks;
            // Check the float evaluator against a double-precision Bezier
            // reference rather than reproducing its polynomial calculation.
            auto &b=d.blocks[i]; double t=b.ticks/20000.0;
            for(unsigned sample=0;sample<=1000;sample++) {
                uint32_t tick=b.ticks*sample/1000; double u=(double)tick/b.ticks;
                double c[6]={(double)b.p0,b.p0+b.v0*t/5,b.p0+2*b.v0*t/5,
                    b.p1-2*b.v1*t/5,b.p1-b.v1*t/5,(double)b.p1};
                for(unsigned degree=5;degree>0;degree--)
                    for(unsigned k=0;k<degree;k++) c[k]=c[k]*(1-u)+c[k+1]*u;
                int32_t p,v; BufferedPath::Evaluate(b,tick,p,v);
                CHECK(std::abs(p-c[0])<8);
            }
        }
        CHECK(d.Start((uint8_t)count,42,repeat!=0));
    }
    unsigned laps=repeat?3:1;
    for(unsigned tick=0;tick<400+lapTicks*laps;tick++) {
        for(unsigned axis=0;axis<axes;axis++) {
            auto &d=drives[axis];
            if(tick%100==0) d.KeepAlive();
            d.Fill(); d.Tick();
            CHECK(d.error==0 && d.index==drives[0].index);
        }
        if(tick>=400 && (tick+1-400)%lapTicks==0)
            for(unsigned axis=0;axis<axes;axis++)
                CHECK(drives[axis].position==step(segments.back().end[axis]));
    }
    for(unsigned axis=0;axis<axes;axis++) CHECK(drives[axis].state==(repeat?2:3));
    std::cout<<"MULTI AXIS WIRE CHECKS PASSED: "<<checks<<"; axes="<<axes<<"; laps="<<laps<<"\n";
}
