import test from 'node:test';
import assert from 'node:assert/strict';
import { loadFixture } from './helpers/modelFixture.js';
import { moveJointTo } from '../robotMotion.js';
import { createRobotSafetyGuard } from '../robotSafety.js';
import { JXS6000_SAFETY_OPTIONS } from '../robotConfig.js';

test('a large J4 slider jump stops at the last safe angle and can return to zero', () => {
  const { robot, joints } = loadFixture();
  const guard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  assert.equal(moveJointTo(robot, 'joint_4', -Math.PI / 2, guard)?.type, 'self');
  assert.ok(joints.joint_4.angle >= -60 * Math.PI / 180 - 1e-9);
  assert.ok(joints.joint_4.angle <= -56 * Math.PI / 180 + 1e-9);
  assert.equal(guard(), null);
  assert.equal(moveJointTo(robot, 'joint_4', 0, guard), null);
  assert.equal(joints.joint_4.angle, 0);
});

test('J5 traverses its full range through the same collision checks used by sliders', () => {
  const { robot, joints } = loadFixture();
  const guard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  for (const target of [-Math.PI / 2, Math.PI * 2, 0]) {
    assert.equal(moveJointTo(robot, 'joint_5', target, guard), null);
    assert.ok(Math.abs(joints.joint_5.angle - target) < 1e-9);
  }
});
