// No serial connection or motor command: measure the shipped collision model.
import { performance } from 'node:perf_hooks';
import { loadFixture } from '../tests/helpers/modelFixture.js';
import { createRobotSafetyGuard } from '../robotSafety.js';
import { JXS6000_SAFETY_OPTIONS } from '../robotConfig.js';
import { validatePath } from '../hmiBridge.js';

const { robot } = loadFixture();
const names = Array.from({ length: 6 }, (_, i) => `joint_${i+1}`);
const check = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
const passes = Number(process.argv[2] || 1);
const results = [];
for (const angle of [10, 150]) {
  // Same zero-entry/exit quintic and sampling bound as the native single move.
  const count = Math.max(20, Math.ceil(5 * angle / .25));
  const points = Array.from({ length: count+1 }, (_, i) => {
    const t = i/count;
    return [0, 0, angle*(10*t**3-15*t**4+6*t**5), 0, 0, 0];
  });
  let checks = 0;
  const guard = () => { checks++; return check(); };
  const start = performance.now();
  for (let pass=0; pass<passes; pass++) {
    for (let offset=0; offset<points.length; offset+=159) {
      const result = validatePath(robot, names, points.slice(offset, offset+160), guard);
      if (!result.ok) throw new Error(result.reason);
      if (offset+160>=points.length) break;
    }
  }
  results.push({angle, samples:points.length, passes, checks, milliseconds:Math.round(performance.now()-start)});
}
console.log(JSON.stringify(results, null, 2));
