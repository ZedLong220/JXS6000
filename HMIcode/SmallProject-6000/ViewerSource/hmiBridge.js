import { MathUtils } from 'three';
import { createRobotSafetyGuard } from './robotSafety.js';
import { JXS6000_SAFETY_OPTIONS } from './robotConfig.js';

export function validateMotion(robot, names, from, to, guard) {
  if (![from, to].every(values => Array.isArray(values) && values.length === names.length && values.every(Number.isFinite))) {
    return { ok: false, reason: '需要六个有限关节角度' };
  }
  if (to.some((value, i) => value < MathUtils.radToDeg(robot.joints[names[i]].limit.lower) - 1e-6 ||
    value > MathUtils.radToDeg(robot.joints[names[i]].limit.upper) + 1e-6)) return { ok: false, reason: '目标角度超出 URDF 限位' };
  const saved = names.map(name => robot.joints[name].angle);
  try {
    const steps = Math.max(1, Math.ceil(Math.max(...to.map((v, i) => Math.abs(v - from[i]))) / 2));
    for (let step = 0; step <= steps; step++) {
      names.forEach((name, i) => robot.joints[name].setJointValue(MathUtils.degToRad(from[i] + (to[i] - from[i]) * step / steps)));
      const violation = guard();
      if (violation) return { ok: false, reason: violation.type === 'ground' ? '运动路径穿地' : `运动路径自碰撞（${violation.bodyA} / ${violation.bodyB}）` };
    }
    return { ok: true };
  } finally {
    names.forEach((name, i) => robot.joints[name].setJointValue(saved[i]));
    robot.updateMatrixWorld(true);
  }
}

// Validate the actual sampled spline, including every interval between its
// samples. Save and restore the preview even when a middle sample fails.
export function validatePath(robot, names, points, guard) {
  if (!Array.isArray(points) || points.length === 0 || points.length > 20000 ||
    points.some(q => !Array.isArray(q) || q.length !== names.length || q.some(v => !Number.isFinite(v)))) {
    return { ok: false, reason: '连续路径数据无效' };
  }
  const saved = names.map(name => robot.joints[name].angle);
  const valid = q => q.every((v,i) => v >= MathUtils.radToDeg(robot.joints[names[i]].limit.lower)-1e-6 &&
    v <= MathUtils.radToDeg(robot.joints[names[i]].limit.upper)+1e-6);
  try {
    for (let i=0;i<points.length;i++) {
      const a = points[Math.max(0,i-1)], b = points[i];
      if (!valid(a) || !valid(b)) return { ok: false, reason: '连续路径超出 URDF 限位' };
      const steps = Math.max(1,Math.ceil(Math.max(...b.map((v,j)=>Math.abs(v-a[j])))/.25));
      // The previous interval already checked its end (= this interval's
      // start). Keep every distinct sample and all .25-degree subdivisions.
      if (i>0 && a.every((v,j)=>v===b[j])) continue;
      for (let k=i===0?steps:1;k<=steps;k++) {
        names.forEach((name,j)=>robot.joints[name].setJointValue(MathUtils.degToRad(a[j]+(b[j]-a[j])*k/steps)));
        const violation = guard();
        if (violation) return { ok:false, reason:`连续曲线 J2=${(a[1]+(b[1]-a[1])*k/steps).toFixed(3)}°、J3=${(a[2]+(b[2]-a[2])*k/steps).toFixed(3)}°附近` +
          (violation.type === 'ground' ? '穿地' : `自碰撞（${violation.bodyA} / ${violation.bodyB}）`) };
      }
    }
    return { ok:true };
  } finally {
    names.forEach((name,i)=>robot.joints[name].setJointValue(saved[i]));
    robot.updateMatrixWorld(true);
  }
}

export function setupHmiBridge(robot, controls) {
  const webview = window.chrome?.webview;
  if (!webview) return () => {};
  const names = Object.keys(robot.joints).filter(name => robot.joints[name].jointType !== 'fixed')
    .sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
  const guard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  let last = '';
  let initialized = false;
  webview.addEventListener('message', event => {
    const message = event.data;
    if (!message || typeof message !== 'object') return;
    if (message.type === 'setJoints') initialized = controls.setJointValues(message.angles);
    if (message.type === 'validateMotion') {
      const result = validateMotion(robot, names, message.from, message.to, guard);
      webview.postMessage({ type: 'validationResult', requestId: message.requestId, ...result });
    }
    if (message.type === 'validatePath') {
      const result = validatePath(robot,names,message.points,guard);
      webview.postMessage({type:'validationResult',requestId:message.requestId,...result});
    }
  });
  webview.postMessage({ type: 'ready' });
  return () => {
    if (!initialized) return;
    const angles = names.map(name => MathUtils.radToDeg(robot.joints[name].angle));
    const key = JSON.stringify(angles);
    if (key === last) return;
    last = key;
    webview.postMessage({ type: 'previewChanged', angles });
  };
}
