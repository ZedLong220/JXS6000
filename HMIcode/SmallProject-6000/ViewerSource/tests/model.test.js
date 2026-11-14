import test from 'node:test';
import assert from 'node:assert/strict';
import { Vector3 } from 'three';
import { loadFixture } from './helpers/modelFixture.js';
import { createRobotSafetyGuard } from '../robotSafety.js';
import { JXS6000_SAFETY_OPTIONS } from '../robotConfig.js';

test('JXS6000 assembled zero pose needs no hidden collision exception', () => {
  const { robot } = loadFixture();
  assert.equal(createRobotSafetyGuard(robot)(), null);
  assert.equal(createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS)(), null);
});

test('shipped JXS6000 zero pose is usable and tool0 matches the URDF', () => {
  const { robot, links } = loadFixture();
  assert.equal(createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS)(), null);
  const tool = robot.worldToLocal(links.tool0.getWorldPosition(new Vector3()));
  assert.ok(tool.distanceTo(new Vector3(.0575, .08673, .3726)) < 1e-8);
});

test('all six shipped joint axes can move at least one degree from zero', () => {
  const { robot, joints } = loadFixture();
  const guard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  for (let index = 1; index <= 6; index += 1) {
    for (const angle of [-Math.PI / 180, Math.PI / 180]) {
      joints[`joint_${index}`](angle);
      assert.equal(guard(), null, `joint_${index} at ${angle} radians`);
    }
    joints[`joint_${index}`](0);
  }
});

test('J4 and J5 sweep useful ranges with collision blocking still enabled', () => {
  const { robot, joints } = loadFixture();
  const guard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  for (const [name, min, max] of [['joint_4', -140, 50], ['joint_5', -90, 360]]) {
    for (let degrees = min; degrees <= max; degrees += 1) {
      joints[name](degrees * Math.PI / 180);
      assert.equal(guard(), null, `${name} at ${degrees} degrees`);
    }
    joints[name](0);
  }
});

test('a checkpoint near the requested J2/J3 limits remains collision free', () => {
  const { robot, joints } = loadFixture();
  const guard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  joints.joint_2(100 * Math.PI / 180);
  joints.joint_3(-300 * Math.PI / 180);
  joints.joint_4(50 * Math.PI / 180);
  assert.equal(guard(), null);
});

test('the reversed J2/J3 model still blocks unsafe intermediate poses', () => {
  const { robot, joints } = loadFixture();
  const guard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  joints.joint_2(-100 * Math.PI / 180);
  assert.deepEqual(guard(), { type: 'ground', body: 'link_2' });
  joints.joint_2(0);
  joints.joint_3(10 * Math.PI / 180);
  assert.deepEqual(guard(), { type: 'self', bodyA: 'link_1', bodyB: 'link_3' });
});

test('new S6 visual and collision meshes share the supplied detail-free geometry', () => {
  const { robot, joints, links } = loadFixture();
  const collisionGuard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  const visualGuard = createRobotSafetyGuard(robot, { ...JXS6000_SAFETY_OPTIONS, geometryMode: 'visual' });
  const frames = links.link_5.children;
  const visual = frames.find(frame => !frame.isURDFCollider).children[0].geometry;
  const collision = frames.find(frame => frame.isURDFCollider).children[0].geometry;
  assert.equal(visual.attributes.position.count / 3, 2846);
  assert.equal(collision.attributes.position.count / 3, 2846);
  assert.deepEqual([...visual.attributes.position.array], [...collision.attributes.position.array]);
  for (const name of ['joint_4', 'joint_5']) {
    joints[name](Math.PI / 180);
    assert.deepEqual(visualGuard(), collisionGuard());
    joints[name](0);
  }
});

test('major JXS6000 wrist collisions remain blocked', () => {
  const { robot, joints } = loadFixture();
  const guard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  joints.joint_4(60 * Math.PI / 180);
  assert.deepEqual(guard(), { type: 'self', bodyA: 'link_2', bodyB: 'link_5' });
  joints.joint_4(-155 * Math.PI / 180);
  assert.deepEqual(guard(), { type: 'self', bodyA: 'link_3', bodyB: 'link_5' });
  joints.joint_4(50 * Math.PI / 180);
  assert.equal(guard(), null);
});

test('J1 leaves S1 fixed and J6 rotates tool0 without rotating the S6 housing', () => {
  const { robot, links, joints } = loadFixture();
  robot.updateMatrixWorld(true);
  const base = robot.children.find(child => child.children.some(object => object.isMesh));
  const basePose = base.matrixWorld.clone();
  joints.joint_1(.2);
  robot.updateMatrixWorld(true);
  assert.ok(basePose.equals(base.matrixWorld));
  const housing = links.link_5.matrixWorld.clone();
  const tool = links.tool0.matrixWorld.clone();
  joints.joint_6(.3);
  robot.updateMatrixWorld(true);
  assert.ok(housing.equals(links.link_5.matrixWorld));
  assert.ok(!tool.equals(links.tool0.matrixWorld));
});
