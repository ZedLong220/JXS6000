#ifndef BUFFERED_PATH_H
#define BUFFERED_PATH_H
#include <cstdint>
#include <cmath>
#include <cstring>
#include <atomic>

/* Position is in internal encoder steps; velocity in steps/s.
   Six-point Bezier form is equivalent to quintic Hermite with zero knot
   acceleration. All coefficients are prepared while the motor is stopped. */
class BufferedPath {
public:
    static const unsigned Capacity = 32;
    struct Block {
        int32_t p0, p1, v0, v1;
        uint32_t ticks;
        float c[6];
    };
    Block blocks[Capacity]{}, first{};
    volatile uint8_t state = 0, count = 0, index = 0, error = 0;
    volatile uint16_t session = 0;
    volatile uint32_t heartbeat = 0;
    int32_t position = 0, velocity = 0;

    void Clear(uint16_t id = 0) {
        state = 0; ++generation; count = index = error = 0; session = id;
        heartbeat = 0; phase = waitTicks = remaining = 0;
        head=tail=0;
    }
    void Fail(uint8_t code) { error = code; state = 4; velocity = 0; }
    bool Active() const { return state == 2; }
    void KeepAlive() { heartbeat = 0; }
    static float Bound(float *b, unsigned degree, unsigned depth) {
        if (depth == 0) {
            float value = 0;
            for (unsigned i=0;i<=degree;++i) value = fmaxf(value, fabsf(b[i]));
            return value;
        }
        float row[6], left[6], right[6];
        for(unsigned i=0;i<=degree;++i) row[i]=b[i];
        left[0]=row[0]; right[degree]=row[degree];
        for(unsigned k=1;k<=degree;++k) {
            for(unsigned i=0;i<=degree-k;++i) row[i]=(row[i]+row[i+1])*0.5f;
            left[k]=row[0]; right[degree-k]=row[degree-k];
        }
        return fmaxf(Bound(left,degree,depth-1),Bound(right,degree,depth-1));
    }
    static bool Prepare(Block &b, int32_t p0, int32_t p1, int32_t v0,
                        int32_t v1, float seconds, float vmax, float amax) {
        if (!std::isfinite(seconds) || seconds<0.05f || seconds>120.0f ||
            vmax<=0 || amax<=0) return false;
        b.p0=p0; b.p1=p1; b.v0=v0; b.v1=v1;
        b.ticks=(uint32_t)lroundf(seconds*1000.0f)*20U;
        float t=b.ticks/20000.0f;
        float d=(float)((int64_t)p1-p0), m0=v0*t, m1=v1*t;
        float controls[6]={0,m0/5,2*m0/5,d-2*m1/5,d-m1/5,d};
        for(unsigned i=0;i<6;++i)
            if((double)p0+controls[i]<INT32_MIN || (double)p0+controls[i]>INT32_MAX) return false;
        float vb[5], ab[4];
        for(unsigned i=0;i<5;++i) vb[i]=5*(controls[i+1]-controls[i])/t;
        for(unsigned i=0;i<4;++i) ab[i]=4*(vb[i+1]-vb[i])/t;
        if(Bound(vb,4,4)>vmax*1.001f || Bound(ab,3,4)>amax*1.001f) return false;
        b.c[0]=0; b.c[1]=m0; b.c[2]=0;
        b.c[3]=10*d-6*m0-4*m1;
        b.c[4]=-15*d+8*m0+7*m1;
        b.c[5]=6*d-3*m0-3*m1;
        return true;
    }
    bool Append(uint8_t seq, uint16_t id, int32_t p0, int32_t p1,
                int32_t v0, int32_t v1, float seconds, float vmax, float amax,
                uint32_t (*enter)() = nullptr, void (*leave)(uint32_t) = nullptr) {
        if(Active() || state==4 || id!=session || seq!=count || count>=Capacity) return false;
        if(count && (blocks[count-1].p1!=p0 || blocks[count-1].v1!=v0)) return Reject(3);
        uint32_t epoch=generation;
        Block prepared{}, firstPrepared{};
        bool ok=Prepare(prepared,p0,p1,v0,v1,seconds,vmax,amax);
        if(seq==0) ok=ok && Prepare(firstPrepared,p0,p1,0,v1,seconds,vmax,amax);
        uint32_t key=enter?enter():0;
        if(generation!=epoch || id!=session || count!=seq || Active() || state==4) { if(leave) leave(key); return false; }
        if(ok) { blocks[count]=prepared; if(seq==0) first=firstPrepared; ++count; state=1; }
        else Reject(3);
        if(leave) leave(key);
        return ok;
    }
    bool Start(uint8_t expected, uint16_t id, bool repeat) {
        if(state!=1 || count==0 || expected!=count || id!=session) return Reject(3);
        if(blocks[count-1].p1!=blocks[0].p0 && repeat) return Reject(3);
        if(repeat && blocks[count-1].v1!=blocks[0].v0) return Reject(3);
        if(!repeat && blocks[count-1].v1!=0) return Reject(3);
        repeating=repeat; firstPass=true; index=0;
        position=blocks[0].p0; velocity=0; phase=0;
        waitTicks=400; remaining=0; heartbeat=0;
        ++generation; head=tail=0; producerIndex=0; producerPhase=0;
        producerFirst=true; producerDone=false;
        state=2;
        return true;
    }
    // Main-loop producer: software float never runs in the 20 kHz ISR.
    // The optional lock protects only publication against a CAN reset/start.
    void Fill(uint32_t (*enter)() = nullptr, void (*leave)(uint32_t) = nullptr) {
        uint32_t epoch=generation;
        while(state==2 && !producerDone) {
            uint8_t next=(uint8_t)((head+1U)%SampleCapacity);
            if(next==tail) return;
            const Block &b=(producerFirst && producerIndex==0)?first:blocks[producerIndex];
            Sample s{};
            uint32_t tick=producerPhase+20U;
            if(tick>b.ticks) tick=b.ticks;
            Evaluate(b,tick,s.p,s.v); s.index=producerIndex;
            s.finish=(tick==b.ticks && producerIndex+1U==count && !repeating)?1U:0U;
            uint32_t key=enter?enter():0;
            if(generation!=epoch || state!=2) { if(leave) leave(key); return; }
            queue[head]=s;
            std::atomic_thread_fence(std::memory_order_release);
            head=next;
            producerPhase=tick;
            if(tick==b.ticks) {
                producerPhase=0;
                if(producerIndex+1U<count) ++producerIndex;
                else if(repeating) { producerIndex=0; producerFirst=false; }
                else producerDone=true;
            }
            if(leave) leave(key);
        }
    }
    void Tick() {
        if(state!=2) return;
        if(++heartbeat>10000U) { Fail(1); return; } // 500 ms CAN watchdog
        if(waitTicks) { --waitTicks; return; }
        if(remaining==0) {
            if(tail==head) { Fail(4); return; } // main loop starved
            std::atomic_thread_fence(std::memory_order_acquire);
            Sample s=queue[tail];
            tail=(uint8_t)((tail+1U)%SampleCapacity);
            index=s.index; finishSample=s.finish;
            samples=remaining=20U;
            dp=s.p-position; dv=s.v-velocity; pr=vr=0;
        }
        pr+=dp; position+=pr/(int32_t)samples; pr%=samplesSigned();
        vr+=dv; velocity+=vr/(int32_t)samples; vr%=samplesSigned();
        --remaining; ++phase;
        if(remaining==0 && finishSample) { state=3; velocity=0; }
    }
    static void Evaluate(const Block &b, uint32_t tick, int32_t &p, int32_t &v) {
        if(tick>=b.ticks) { p=b.p1; v=b.v1; return; }
        float s=(float)tick/(float)b.ticks;
        float relative=((((b.c[5]*s+b.c[4])*s+b.c[3])*s+b.c[2])*s+b.c[1])*s;
        p=(int32_t)((int64_t)b.p0+llroundf(relative));
        float derivative=(((5*b.c[5]*s+4*b.c[4])*s+3*b.c[3])*s+2*b.c[2])*s+b.c[1];
        v=(int32_t)lroundf(derivative*(20000.0f/(float)b.ticks));
    }
private:
    uint32_t phase=0, waitTicks=0;
    unsigned remaining=0, samples=1;
    int32_t dp=0,dv=0,pr=0,vr=0;
    bool repeating=false, firstPass=true;
    struct Sample { int32_t p,v; uint8_t index,finish; };
    static const unsigned SampleCapacity=64;
    Sample queue[SampleCapacity]{};
    volatile uint8_t head=0,tail=0;
    volatile uint32_t generation=0;
    uint8_t producerIndex=0,finishSample=0;
    uint32_t producerPhase=0;
    bool producerFirst=true,producerDone=false;
    int32_t samplesSigned() const { return (int32_t)samples; }
    bool Reject(uint8_t code) { Fail(code); return false; }
};
#endif
