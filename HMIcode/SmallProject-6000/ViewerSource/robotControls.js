import { MathUtils } from 'three';

import { createRobotSafetyGuard } from './robotSafety.js';
import { JXS6000_SAFETY_OPTIONS } from './robotConfig.js';
import { moveJointTo } from './robotMotion.js';

const MOTION_SAMPLE_STEP = MathUtils.degToRad(2);

const KEY_BINDINGS = [
  ['1', 'q'],
  ['2', 'w'],
  ['3', 'e'],
  ['4', 'r'],
  ['5', 't'],
  ['6', 'y'],
];

const JOINT_LABELS = [
  'J1 基座旋转',
  'J2 肩部俯仰',
  'J3 肘部俯仰',
  'J4 腕部俯仰',
  'J5 腕部摆转',
  'J6 法兰旋转',
];

export function setAppStatus(state, text) {
  const status = document.getElementById('modelStatus');
  if (!status) return;
  status.dataset.state = state;
  status.querySelector('span:last-child').textContent = text;
}

function jointValue(joint) {
  if (Number.isFinite(joint.angle)) return joint.angle;
  if (Array.isArray(joint.jointValue)) return joint.jointValue[0] || 0;
  return Number(joint.jointValue) || 0;
}

function limitsFor(joint) {
  if (joint.jointType === 'continuous') return [-Math.PI, Math.PI];
  const lower = Number.isFinite(joint.limit?.lower) ? joint.limit.lower : -Math.PI;
  const upper = Number.isFinite(joint.limit?.upper) ? joint.limit.upper : Math.PI;
  return [lower, upper];
}

function clampToLimits(joint, value) {
  const [lower, upper] = limitsFor(joint);
  return Math.min(upper, Math.max(lower, value));
}

export function setupRobotControls(robot) {
  const jointNames = Object.keys(robot.joints)
    .filter(name => robot.joints[name].jointType !== 'fixed')
    .sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
  const controlsRoot = document.getElementById('jointControls');
  const keyState = new Set();
  const sliderByJoint = new Map();
  const valueByJoint = new Map();
  const detectSafetyViolation = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  const motionAlert = document.getElementById('motionAlert');
  const collisionBlocking = document.getElementById('collisionBlocking');
  let speed = MathUtils.degToRad(0.75);

  controlsRoot.replaceChildren();

  jointNames.forEach((name, index) => {
    const joint = robot.joints[name];
    const [lower, upper] = limitsFor(joint);
    const row = document.createElement('div');
    row.className = 'joint-row';

    const top = document.createElement('div');
    top.className = 'joint-row__top';
    const label = document.createElement('label');
    label.htmlFor = `joint-${name}`;
    label.textContent = JOINT_LABELS[index] || name;
    const value = document.createElement('output');
    value.textContent = '0.0°';
    top.append(label, value);

    const input = document.createElement('input');
    input.id = `joint-${name}`;
    input.type = 'range';
    input.min = MathUtils.radToDeg(lower).toFixed(2);
    input.max = MathUtils.radToDeg(upper).toFixed(2);
    input.step = '0.1';
    input.value = MathUtils.radToDeg(jointValue(joint)).toFixed(2);
    input.setAttribute('aria-label', label.textContent);
    input.addEventListener('input', () => {
      const radians = MathUtils.degToRad(Number(input.value));
      trySetJointValue(name, radians);
    });

    const keys = document.createElement('div');
    keys.className = 'joint-row__keys';
    keys.textContent = `${KEY_BINDINGS[index]?.[0] || ''} / ${KEY_BINDINGS[index]?.[1]?.toUpperCase() || ''}`;

    row.append(top, input, keys);
    controlsRoot.appendChild(row);
    sliderByJoint.set(name, input);
    valueByJoint.set(name, value);
  });

  function updateUi(name) {
    const angle = jointValue(robot.joints[name]);
    const degrees = MathUtils.radToDeg(angle);
    sliderByJoint.get(name).value = degrees;
    valueByJoint.get(name).textContent = `${degrees.toFixed(1)}°`;
  }

  function showMotionWarning(violation, blocked = true) {
    const detail = violation.type === 'ground'
      ? '检测到穿地'
      : `检测到自碰撞（${violation.bodyA} / ${violation.bodyB}）`;
    const text = `${detail}，${blocked ? '已阻止继续运动' : '当前仅提示'}`;
    motionAlert.textContent = text;
    motionAlert.hidden = false;
    setAppStatus('error', text);
  }

  function clearMotionWarning() {
    motionAlert.hidden = true;
    setAppStatus('ready', '模型已就绪');
  }

  function trySetJointValue(name, requestedValue) {
    const joint = robot.joints[name];
    const nextValue = clampToLimits(joint, requestedValue);

    if (!collisionBlocking.checked) {
      joint.setJointValue(nextValue);
      updateUi(name);
      const violation = detectSafetyViolation();
      if (violation) showMotionWarning(violation, false);
      else clearMotionWarning();
      return true;
    }

    const violation = moveJointTo(robot, name, nextValue, detectSafetyViolation, MOTION_SAMPLE_STEP);
    updateUi(name);
    if (violation) {
      showMotionWarning(violation);
      return false;
    }
    clearMotionWarning();
    return true;
  }

  function resetJoints() {
    const previousValues = new Map(jointNames.map(name => [name, jointValue(robot.joints[name])]));
    const targetValues = new Map(jointNames.map(name => [name, clampToLimits(robot.joints[name], 0)]));
    if (!collisionBlocking.checked) {
      targetValues.forEach((angle, name) => robot.joints[name].setJointValue(angle));
      jointNames.forEach(updateUi);
      const violation = detectSafetyViolation();
      if (violation) showMotionWarning(violation, false);
      else clearMotionWarning();
      return;
    }
    const maxDelta = Math.max(...jointNames.map(name => Math.abs(targetValues.get(name) - previousValues.get(name))));
    const steps = Math.max(1, Math.ceil(maxDelta / MOTION_SAMPLE_STEP));

    for (let step = 1; step <= steps; step += 1) {
      jointNames.forEach(name => {
        const start = previousValues.get(name);
        robot.joints[name].setJointValue(start + (targetValues.get(name) - start) * step / steps);
      });
      const violation = detectSafetyViolation();
      if (violation) {
        previousValues.forEach((angle, name) => robot.joints[name].setJointValue(angle));
        robot.updateMatrixWorld(true);
        jointNames.forEach(updateUi);
        showMotionWarning(violation);
        return;
      }
    }

    jointNames.forEach(updateUi);
    clearMotionWarning();
  }

  document.getElementById('resetJoints').addEventListener('click', resetJoints);
  collisionBlocking.addEventListener('change', () => {
    const violation = detectSafetyViolation();
    if (violation) {
      showMotionWarning(violation, false);
      if (collisionBlocking.checked) {
        motionAlert.textContent += '；请先取消拦截并移出碰撞位置';
      }
    } else clearMotionWarning();
  });
  document.getElementById('speedControl').addEventListener('input', event => {
    const degrees = Number(event.target.value);
    speed = MathUtils.degToRad(degrees);
    document.getElementById('speedValue').textContent = `${degrees.toFixed(2)}°`;
  });

  window.addEventListener('keydown', event => {
    if (event.repeat || event.target instanceof HTMLInputElement) return;
    keyState.add(event.key.toLowerCase());
  });
  window.addEventListener('keyup', event => keyState.delete(event.key.toLowerCase()));
  window.addEventListener('blur', () => keyState.clear());

  resetJoints();

  const update = () => {
    KEY_BINDINGS.forEach((keys, index) => {
      if (!jointNames[index]) return;
      const direction = (keyState.has(keys[1]) ? 1 : 0) - (keyState.has(keys[0]) ? 1 : 0);
      if (!direction) return;
      const name = jointNames[index];
      const joint = robot.joints[name];
      trySetJointValue(name, jointValue(joint) + direction * speed);
    });
    robot.updateMatrixWorld(true);
  };
  // A native FK result changes only the preview. Actual motor commands are
  // separately checked by validateMotion/validatePath before transmission.
  update.setJointValues = angles => {
    if (!Array.isArray(angles) || angles.length !== jointNames.length ||
      angles.some((v,i) => !Number.isFinite(v) || MathUtils.degToRad(v) < limitsFor(robot.joints[jointNames[i]])[0]-1e-8 ||
        MathUtils.degToRad(v) > limitsFor(robot.joints[jointNames[i]])[1]+1e-8)) return false;
    jointNames.forEach((name,i)=>robot.joints[name].setJointValue(MathUtils.degToRad(angles[i])));
    jointNames.forEach(updateUi);
    robot.updateMatrixWorld(true);
    const violation=detectSafetyViolation();
    if (violation) showMotionWarning(violation,false);
    else clearMotionWarning();
    return true;
  };
  return update;
}
