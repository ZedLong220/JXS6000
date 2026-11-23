import { readFileSync,writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';

if(process.argv.length!==4)throw new Error('Usage: node checkGeometry.mjs <repository root> <validation output directory>');
const root=resolve(process.argv[2]),output=resolve(process.argv[3]);
const viewer=pathToFileURL(resolve(root,'HMIcode/SmallProject-6000/ViewerSource')+'/');
const { loadFixture }=await import(new URL('tests/helpers/modelFixture.js',viewer));
const { createRobotSafetyGuard }=await import(new URL('robotSafety.js',viewer));
const { JXS6000_SAFETY_OPTIONS }=await import(new URL('robotConfig.js',viewer));
const { validatePath }=await import(new URL('hmiBridge.js',viewer));
const read=name=>JSON.parse(readFileSync(resolve(output,name),'utf8'));
const plan=read('fixed-arc-plan.json'),rows=read('actual-arc-samples.json').filter(Boolean);
const urdfHash=createHash('sha256').update(readFileSync(new URL('public/URDF/jxs6000.urdf',viewer))).digest('hex');
if(plan.urdfSha256!==urdfHash)throw new Error('Generated arc does not match the Viewer URDF');
const {robot,links,joints}=loadFixture();
const names=Array.from({length:6},(_,i)=>`joint_${i+1}`);
const guard=createRobotSafetyGuard(robot,JXS6000_SAFETY_OPTIONS);
const dot=(a,b)=>a.reduce((s,v,i)=>s+v*b[i],0);
let maximumCircleErrorMm=0,maximumPitchSumErrorDegrees=0,maximumJointStepDegrees=0,j1ReverseSteps=0;
let minPhase=Infinity,maxPhase=-Infinity;
for(let k=0;k<rows.length;k++) {
  const q=rows[k].slice(1);
  q.forEach((v,i)=>joints[names[i]].setJointValue(v*Math.PI/180));robot.updateMatrixWorld(true);
  const p=links.tool0.getWorldPosition(robot.position.clone());
  const xyz=[p.x*1000,-p.z*1000,p.y*1000],delta=xyz.map((v,i)=>v-plan.center[i]);
  const x=dot(delta,plan.ex),y=dot(delta,plan.ey),phase=Math.atan2(y,x);
  const outPlane=Math.sqrt(Math.max(0,dot(delta,delta)-x*x-y*y));
  maximumCircleErrorMm=Math.max(maximumCircleErrorMm,Math.hypot(Math.hypot(x,y)-plan.radiusMm,outPlane));
  maximumPitchSumErrorDegrees=Math.max(maximumPitchSumErrorDegrees,Math.abs(q[1]+q[2]+q[3]+160));
  if(phase>-.01){minPhase=Math.min(minPhase,phase);maxPhase=Math.max(maxPhase,phase);}
  if(k) {
    maximumJointStepDegrees=Math.max(maximumJointStepDegrees,...q.map((v,i)=>Math.abs(v-rows[k-1][i+1])));
    if(q[0]>rows[k-1][1]+1e-9)j1ReverseSteps++;
  }
}
let collisionChecks=0;
const validation=validatePath(robot,names,rows.map(row=>row.slice(1)),()=>{collisionChecks++;return guard();});
const summary={
  source:'Actual master CAN frames loaded into five production BufferedPath executors at 20kHz, sampled each 1ms',
  samples:rows.length,maximumCircleErrorMm,maximumPitchSumErrorDegrees,maximumJointStepDegrees,j1ReverseSteps,
  arcAngleDegrees:(maxPhase-minPhase)*180/Math.PI,collisionValidation:validation,collisionChecks,
  plannedDurationSeconds:plan.totalSeconds,velocityBoundRevPerSecond:plan.peakVelocityRevPerSecond,
  accelerationBoundRevPerSecondSquared:plan.peakAccelerationRevPerSecondSquared
};
writeFileSync(resolve(output,'geometry-summary.json'),JSON.stringify(summary,null,2));
console.log(JSON.stringify(summary,null,2));
if(!validation.ok || maximumCircleErrorMm>.5 || j1ReverseSteps || maximumJointStepDegrees>.25)process.exitCode=1;
