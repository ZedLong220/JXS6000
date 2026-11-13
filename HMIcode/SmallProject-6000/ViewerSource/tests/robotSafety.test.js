import test from 'node:test';
import assert from 'node:assert/strict';
import { BoxGeometry, BufferGeometry, Float32BufferAttribute, Group, Mesh, MathUtils } from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { createRobotSafetyGuard } from '../robotSafety.js';
import { createReferenceGuard } from './helpers/referenceGuard.js';
import { loadFixture } from './helpers/modelFixture.js';
import { JXS6000_SAFETY_OPTIONS } from '../robotConfig.js';

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

test('rotated conservative box below ground does not reject an above-ground triangle', () => {
  const robot = new Group(), link = new Group();
  robot.name = 'base_link'; link.name = 'moving'; robot.add(link);
  robot.links = { base_link:robot, moving:link };
  const geometry = new BufferGeometry();
  geometry.setAttribute('position',new Float32BufferAttribute([0,1,0, 1,0,0, 1,1,0],3));
  const mesh = new Mesh(geometry); mesh.rotation.z=Math.PI/4; mesh.position.y=-.5; link.add(mesh);
  const guard = createRobotSafetyGuard(robot);
  assert.equal(guard(),null);
  link.position.y=-.3;
  assert.deepEqual(guard(),{type:'ground',body:'moving'});
});

test('optimized bounds preserve reference collision results throughout the JXS6000 joint ranges', () => {
  const {robot,joints} = loadFixture();
  const guard = createRobotSafetyGuard(robot,JXS6000_SAFETY_OPTIONS);
  const reference = createReferenceGuard(robot,JXS6000_SAFETY_OPTIONS);
  const poses = [];
  for(const j2 of [-250,-90,0,90,180]) for(const j3 of [-180,-130,-40,0,10,40,90,150,240,330])
    poses.push([0,j2,j3,0,0,0]);
  for(let i=0;i<80;i++) poses.push([
    -180+(i*137)%361, -250+(i*73)%431, -180+(i*113)%511,
    -50+(i*29)%191, -90+(i*97)%451, -180+(i*53)%361]);
  for(const q of poses) {
    q.forEach((value,i)=>joints[`joint_${i+1}`].setJointValue(MathUtils.degToRad(value)));
    assert.deepEqual(guard(),reference(),`pose ${q.join(',')}`);
  }
});
