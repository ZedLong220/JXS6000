import test from 'node:test';
import assert from 'node:assert/strict';
import { loadFixture } from './helpers/modelFixture.js';
import { createRobotSafetyGuard } from '../robotSafety.js';
import { JXS6000_SAFETY_OPTIONS } from '../robotConfig.js';
import { validateMotion, validatePath } from '../hmiBridge.js';
import fs from 'node:fs';

test('all six axes including physical J6 follow the actual rounded curve', () => {
  const samples=JSON.parse(fs.readFileSync(new URL('../../Tests/bin/Debug/net10.0/six-axis-samples.json',import.meta.url),'utf8'));
  const {robot}=loadFixture();
  const names=Array.from({length:6},(_,i)=>`joint_${i+1}`);
  for(let axis=0;axis<6;axis++) assert.ok(samples.some(q=>Math.abs(q[axis])>0.1));
  const guard=createRobotSafetyGuard(robot,JXS6000_SAFETY_OPTIONS);
  const result=validatePath(robot,names,samples,guard);
  assert.equal(result.ok,true,result.reason);
  assert.equal(validateMotion(robot,names,[0,0,0,0,0,-180],[0,0,0,0,0,180],guard).ok,true);
  assert.equal(validateMotion(robot,names,[0,0,0,0,0,0],[0,0,0,0,0,181],guard).ok,false);
});

test('five physical axes follow the actual rounded wire samples safely', () => {
  const fixture=new URL('../../Tests/bin/Debug/net10.0/five-axis-samples.json',import.meta.url);
  const samples=JSON.parse(fs.readFileSync(fixture,'utf8'));
  const {robot}=loadFixture();
  const names=Array.from({length:6},(_,i)=>`joint_${i+1}`);
  assert.ok(samples.length>10);
  assert.ok(samples.every(q=>q.length===6 && q[5]===0));
  for(let axis=0;axis<5;axis++) assert.ok(samples.some(q=>Math.abs(q[axis])>0.1));
  const result=validatePath(robot,names,samples,createRobotSafetyGuard(robot,JXS6000_SAFETY_OPTIONS));
  assert.equal(result.ok,true,result.reason);
});

test('HMI motion validation uses the wrist proxy and restores the displayed pose', () => {
  const {robot, joints} = loadFixture();
  const names = Array.from({length: 6}, (_, i) => `joint_${i+1}`);
  names.forEach(name => { joints[name].limit = { lower: -Math.PI, upper: Math.PI }; });
  joints.joint_2.limit = { lower: -250 * Math.PI / 180, upper: Math.PI };
  joints.joint_3.limit = { lower: -Math.PI, upper: 330 * Math.PI / 180 };
  joints.joint_5.limit = { lower: -Math.PI / 2, upper: 2 * Math.PI };
  const guard = createRobotSafetyGuard(robot, JXS6000_SAFETY_OPTIONS);
  joints.joint_1(.1);
  const start = new Array(6).fill(0);
  assert.equal(validateMotion(robot, names, start, [0,0,0,0,360,0], guard).ok, true);
  assert.equal(joints.joint_1.angle, .1);
  assert.equal(joints.joint_5.angle, 0);
  assert.equal(validateMotion(robot, names, start, [0,0,0,-60,0,0], guard).ok, false);
  assert.equal(joints.joint_1.angle, .1);
  assert.equal(joints.joint_4.angle, 0);
  assert.equal(validateMotion(robot, names, start, [0,0,0,NaN,0,0], guard).ok, false);
});

test('batch curve checks detect a middle collision, invalid start and limits, restoring preview', () => {
  const {robot,joints}=loadFixture();
  const names=Array.from({length:6},(_,i)=>`joint_${i+1}`);
  const guard=createRobotSafetyGuard(robot,JXS6000_SAFETY_OPTIONS);
  joints.joint_1(.1);
  assert.equal(validatePath(robot,names,[[0,0,0,0,0,0],[0,0,0,-60,0,0],[0,0,0,0,0,0]],guard).ok,false);
  assert.equal(joints.joint_1.angle,.1);
  assert.equal(validatePath(robot,names,[[0,0,0,0,0,0],[0,0,0,0,361,0]],guard).ok,false);
  assert.equal(validatePath(robot,names,[[0,NaN,0,0,0,0]],guard).ok,false);
});

test('actual wire curves are checked: the old fixed route collides with the new structure, a clear route passes', {
  skip: !fs.existsSync(new URL('../../Tests/bin/Debug/net10.0/continuous-samples.json',import.meta.url)) && 'run KinematicsChecks to generate wire fixture'
}, () => {
  const fixture=new URL('../../Tests/bin/Debug/net10.0/continuous-samples.json',import.meta.url);
  assert.ok(fs.existsSync(fixture),'run KinematicsChecks first to generate the actual wire fixture');
  const data=JSON.parse(fs.readFileSync(fixture,'utf8'));
  const {robot}=loadFixture();
  const names=Array.from({length:6},(_,i)=>`joint_${i+1}`);
  const guard=createRobotSafetyGuard(robot,JXS6000_SAFETY_OPTIONS);
  for(const samples of [data.first,data.samples]) {
    const result=validatePath(robot,names,samples,guard);
    assert.equal(result.ok,false,'old fixed path must be rejected');
    assert.match(result.reason,/link_1 \/ link_3/);
  }
  const result=validatePath(robot,names,data.safe,guard);
  assert.equal(result.ok,true,result.reason);
});
