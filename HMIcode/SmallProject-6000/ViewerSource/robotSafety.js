import { Box3, Matrix4, Vector3 } from 'three';
import { MeshBVH } from 'three-mesh-bvh';

const GROUND_TOLERANCE = 0.001;

// Preserve concave CAD shapes: a convex hull around a folded arm also encloses
// empty space and can report a collision even in its assembled zero pose.
export function createRobotSafetyGuard(robot, { allowedCollisionPairs = [], geometryMode = 'collision' } = {}) {
  const links = new Set(Object.values(robot.links));
  const bodies = new Map();
  const trees = new WeakMap();
  const pairKey = (a, b) => [a, b].sort().join(':');
  const allowed = new Set(allowedCollisionPairs.map(pair => pairKey(...pair)));
  const point = new Vector3();
  const relative = new Matrix4();
  let hasColliders = false;
  robot.traverse(object => { if (object.isURDFCollider) hasColliders = true; });

  robot.traverse(mesh => {
    if (!mesh.isMesh || !mesh.geometry?.attributes?.position) return;
    let link = mesh.parent;
    let collider = false;
    while (link && !links.has(link)) {
      collider ||= Boolean(link.isURDFCollider);
      link = link.parent;
    }
    if (!link) return;
    // Rendering details are not automatically collision geometry. Prefer the
    // URDF <collision> frames, including hidden ones, when they are supplied.
    if (hasColliders && collider !== (geometryMode === 'collision')) return;

    if (!bodies.has(link)) bodies.set(link, { name: link.name, link, meshes: [], box: new Box3() });
    const geometry = mesh.geometry;
    if (!geometry.boundingBox) geometry.computeBoundingBox();
    if (!trees.has(geometry)) {
      const tree = new MeshBVH(geometry);
      geometry.boundsTree = tree;
      trees.set(geometry, tree);
    }
    bodies.get(link).meshes.push({ mesh, geometry, tree: trees.get(geometry), box: new Box3() });
  });

  const bodyList = [...bodies.values()];
  const adjacent = new Set();
  bodyList.forEach(body => {
    let parent = body.link.parent;
    while (parent && !bodies.has(parent)) parent = parent.parent;
    if (parent) adjacent.add(`${bodyList.indexOf(bodies.get(parent))}:${bodyList.indexOf(body)}`);
  });

  return () => {
    robot.updateMatrixWorld(true);
    for (const body of bodyList) {
      body.box.makeEmpty();
      for (const item of body.meshes) {
        // Transform eight cached local box corners for broad-phase rejection.
        // This box encloses the mesh; surface collisions still use the BVH.
        item.box.copy(item.geometry.boundingBox).applyMatrix4(item.mesh.matrixWorld);
        if (body.name !== 'base_link' && item.box.min.y < -GROUND_TOLERANCE) {
          // A rotated box can extend below every real vertex. Retain the exact
          // vertex test near the ground so the conservative box cannot reject
          // a valid pose or change the existing ground tolerance.
          const positions = item.geometry.attributes.position;
          for (let index = 0; index < positions.count; index += 1) {
            point.fromBufferAttribute(positions, index).applyMatrix4(item.mesh.matrixWorld);
            if (point.y < -GROUND_TOLERANCE) return { type: 'ground', body: body.name };
          }
        }
        body.box.union(item.box);
      }
    }

    for (let first = 0; first < bodyList.length; first += 1) {
      for (let second = first + 1; second < bodyList.length; second += 1) {
        if (adjacent.has(`${first}:${second}`) || adjacent.has(`${second}:${first}`)) continue;
        const a = bodyList[first];
        const b = bodyList[second];
        if (allowed.has(pairKey(a.name, b.name))) continue;
        if (!a.box.intersectsBox(b.box)) continue;
        for (const meshA of a.meshes) {
          for (const meshB of b.meshes) {
            if (!meshA.box.intersectsBox(meshB.box)) continue;
            relative.copy(meshA.mesh.matrixWorld).invert().multiply(meshB.mesh.matrixWorld);
            if (meshA.tree.intersectsGeometry(meshB.geometry, relative)) {
              return { type: 'self', bodyA: a.name, bodyB: b.name };
            }
          }
        }
      }
    }
    return null;
  };
}
