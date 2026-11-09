import test from 'node:test';
import assert from 'node:assert/strict';
import { BoxGeometry, Group, Mesh } from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { createRobotSafetyGuard } from '../robotSafety.js';

function fixture() {
  const robot = new Group();
  robot.name = 'base_link';
  const shoulder = new Group();
  shoulder.name = 'link_1';
  const arm = new Group();
  arm.name = 'link_2';
  const flange = new Group();
  flange.name = 'link_6';
  robot.add(shoulder);
  shoulder.add(arm);
  arm.add(flange);
  robot.links = { base_link: robot, link_1: shoulder, link_2: arm, link_6: flange };
  // Two disconnected shapes sharing one exported STL: their convex hull
  // contains the gap occupied by another link in the folded assembly.
  const left = new BoxGeometry(1, 1, 1).translate(-1, 1, 0);
  const right = new BoxGeometry(1, 1, 1).translate(1, 1, 0);
  robot.add(new Mesh(mergeGeometries([left, right])));
  shoulder.add(new Mesh(new BoxGeometry(.1, .1, .1).translate(0, 3, 0)));
  const end = new Mesh(new BoxGeometry(.2, .2, .2));
  end.position.y = 1;
  arm.add(end);
  return { robot, end, shoulder };
}

test('empty space inside a concave assembly is not a self collision', () => {
  const { robot } = fixture();
  assert.equal(createRobotSafetyGuard(robot)(), null);
});

test('non-adjacent transformed mesh surfaces are checked for collisions', () => {
  const { robot, end } = fixture();
  const guard = createRobotSafetyGuard(robot);
  end.position.x = .5;
  const violation = guard();
  assert.equal(violation.type, 'self');
  assert.deepEqual(new Set([violation.bodyA, violation.bodyB]), new Set(['base_link', 'link_2']));
  end.position.x = 0;
  assert.equal(guard(), null);
});

test('ground check catches moving geometry and ignores meshless flange links', () => {
  const { robot, end } = fixture();
  const guard = createRobotSafetyGuard(robot);
  end.position.y = -.2;
  assert.deepEqual(guard(), { type: 'ground', body: 'link_2' });
});

test('mating surfaces on adjacent links do not lock their joint', () => {
  const { robot, shoulder } = fixture();
  shoulder.children.find(child => child.isMesh).position.set(.5, -2, 0);
  assert.equal(createRobotSafetyGuard(robot)(), null);
});
