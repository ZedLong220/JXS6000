#include "../Ctrl/Motor/motion_planner.h"
#include <algorithm>
#include <cstdlib>
#include <iostream>
#include <limits>

static unsigned checks;
#define CHECK(x) do { ++checks; if (!(x)) { std::cerr << "FAIL " << __LINE__ << ": " << #x << "\n"; std::exit(2); } } while (0)

static void check_position_tracker(int direction, int32_t acceleration, float requestedTime)
{
    const int32_t steps = 51200;
    const int32_t distance = (int32_t)(83.333333 * steps);
    MotionPlanner planner;
    MotionPlanner::Config_t config{};
    config.ratedVelocityAcc = acceleration * steps;
    config.ratedVelocityJerk = 20000 * steps; // current Motor constructor
    MotionPlanner::TimedPositionVelocityLimit(distance, requestedTime,
        config.ratedVelocityAcc, 15 * steps, config.ratedVelocity);
    planner.AttachConfig(&config);
    planner.positionTracker.NewTask(0,0);
    int32_t peak = 0;
    unsigned tick;
    for(tick=0;tick<20U*20000U;++tick) {
        planner.positionTracker.CalcSoftGoal(direction*distance);
        peak=std::max(peak,std::abs(planner.positionTracker.go_velocity));
        if(planner.positionTracker.go_location==direction*distance &&
           planner.positionTracker.go_velocity==0) break;
    }
    std::cout << "Position tracker a=" << acceleration << " direction=" << direction
              << " requested=" << requestedTime << " peak=" << (double)peak/steps
              << " time=" << (double)tick/20000 << "\n";
    CHECK(peak<=15*steps);
    CHECK(peak<=config.ratedVelocity);
    CHECK(tick<20U*20000U);
}

int main()
{
    const int32_t steps = 51200;
    const int32_t limit = 15 * steps;
    const int32_t acceleration = 15 * steps;
    const uint64_t j3Distance = (uint64_t)(83.333333 * steps);
    int32_t velocity;

    CHECK(MotionPlanner::TimedPositionVelocityLimit(j3Distance, 6.56f, acceleration, limit, velocity));
    CHECK(velocity > 14.98 * steps && velocity <= limit);
    const double trapezoidTime = (double)j3Distance / velocity + (double)velocity / acceleration;
    CHECK(std::abs(trapezoidTime - 6.56) < 0.0001);
    // Time is acceleration-feasible at 5 s, but would exceed the saved 15 r/s.
    CHECK(!MotionPlanner::TimedPositionVelocityLimit(j3Distance, 5.0f, acceleration, limit, velocity));
    CHECK(velocity == limit);
    CHECK(!MotionPlanner::TimedPositionVelocityLimit(j3Distance, 1.0f, acceleration, limit, velocity));
    CHECK(velocity == limit);
    CHECK(!MotionPlanner::TimedPositionVelocityLimit(j3Distance, 6.56f, 10 * steps, limit, velocity));
    CHECK(velocity == limit);

    // Use the same production calculator for every outbound/return axis.
    const double distances[] = {2.083333,19.444444,83.333333,5.833333,7.5,
                               6.944444,19.444444,83.333333,5.833333,7.5};
    for (double distance : distances) {
        const uint64_t travel = (uint64_t)(distance * steps);
        CHECK(MotionPlanner::TimedPositionVelocityLimit(travel, 6.56f, acceleration, limit, velocity));
        CHECK(velocity > 0 && velocity <= limit);
        CHECK(std::abs((double)travel / velocity + (double)velocity / acceleration - 6.56) < 0.001);
    }
    CHECK(MotionPlanner::TimedPositionVelocityLimit((uint64_t)(4.861111 * steps), 2.0f, acceleration, limit, velocity));
    CHECK(velocity > 0 && velocity <= limit);
    CHECK(MotionPlanner::TimedPositionVelocityLimit(0, 6.56f, acceleration, limit, velocity));
    CHECK(velocity == 0);
    CHECK(MotionPlanner::TimedPositionVelocityLimit(1, 120.0f, acceleration, limit, velocity));
    CHECK(velocity == 1); // no cancellation to a zero speed on tiny moves
    CHECK(!MotionPlanner::TimedPositionVelocityLimit(4000000000ULL, 1.0f, acceleration, limit, velocity));
    CHECK(velocity == limit);
    CHECK(!MotionPlanner::TimedPositionVelocityLimit(j3Distance, std::numeric_limits<float>::quiet_NaN(), acceleration, limit, velocity));
    CHECK(!MotionPlanner::TimedPositionVelocityLimit(j3Distance, 0.0f, acceleration, limit, velocity));
    CHECK(!MotionPlanner::TimedPositionVelocityLimit(j3Distance, 6.56f, 0, limit, velocity));
    CHECK(!MotionPlanner::TimedPositionVelocityLimit(j3Distance, 6.56f, acceleration, 0, velocity));
    CHECK(velocity == 0);
    for(int direction : {-1,1}) {
        check_position_tracker(direction,15,6.56f);
        check_position_tracker(direction,15,5.0f);
        check_position_tracker(direction,100,6.56f);
    }
    std::cout << "TIMED POSITION CHECKS PASSED: " << checks << "\n";
}
