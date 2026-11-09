// Advance to the last safe sample rather than undoing an entire slider jump.
// This lets a request beyond an obstacle visibly approach its collision limit.
export function moveJointTo(robot, name, target, detectViolation, sampleStep = Math.PI / 90) {
  const joint = robot.joints[name];
  const start = joint.angle;
  const steps = Math.max(1, Math.ceil(Math.abs(target - start) / sampleStep));
  let accepted = start;
  for (let step = 1; step <= steps; step++) {
    const angle = start + (target - start) * step / steps;
    joint.setJointValue(angle);
    const violation = detectViolation();
    if (violation) {
      joint.setJointValue(accepted);
      robot.updateMatrixWorld(true);
      return violation;
    }
    accepted = angle;
  }
  return null;
}
