import { readFileSync } from 'node:fs';
import { Euler, Group, Mesh, Vector3 } from 'three';
import { STLLoader } from 'three/examples/jsm/loaders/STLLoader.js';

// Fixture reader for this checked-in URDF. Production uses URDFLoader; these
// tests independently reconstruct its transforms using the actual shipped STL.
const modelRoot = new URL('../../public/URDF/', import.meta.url);
const source = readFileSync(new URL('jxs6000.urdf', modelRoot), 'utf8');
const attribute = (tag, key) => tag.match(new RegExp(`${key}="([^"]+)"`))?.[1];
const tuple = text => text.split(/\s+/).map(Number);
function pose(object, tag) {
  object.position.fromArray(tuple(attribute(tag, 'xyz') || '0 0 0'));
  object.quaternion.setFromEuler(new Euler(...tuple(attribute(tag, 'rpy') || '0 0 0'), 'ZYX'));
}
export function loadFixture() {
  const links = {};
  for (const match of source.matchAll(/<link\b([^>]*)(?:\/>|>([\s\S]*?)<\/link>)/g)) {
    const name = attribute(match[1], 'name');
    const link = new Group();
    link.name = name;
    links[name] = link;
    for (const element of (match[2] || '').matchAll(/<(visual|collision)\b[^>]*>([\s\S]*?)<\/(?:visual|collision)>/g)) {
    const visual = element[2];
    const meshTag = visual.match(/<mesh\b[^>]*\/>/)[0];
    const url = attribute(meshTag, 'filename').replace('package://jxs6000_description/', 'jxs6000_description/');
    const bytes = readFileSync(new URL(url, modelRoot));
    const geometry = new STLLoader().parse(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength));
    const frame = new Group();
    frame.isURDFCollider = element[1] === 'collision';
    if (frame.isURDFCollider) frame.visible = false;
    pose(frame, visual.match(/<origin\b[^>]*\/>/)[0]);
    const mesh = new Mesh(geometry);
    mesh.scale.fromArray(tuple(attribute(meshTag, 'scale')));
    frame.add(mesh);
    link.add(frame);
    }
  }
  const joints = {};
  for (const match of source.matchAll(/<joint\b([^>]*)>([\s\S]*?)<\/joint>/g)) {
    const frame = new Group();
    const name = attribute(match[1], 'name');
    pose(frame, match[2].match(/<origin\b[^>]*\/>/)[0]);
    links[attribute(match[2].match(/<parent\b[^>]*\/>/)[0], 'link')].add(frame);
    frame.add(links[attribute(match[2].match(/<child\b[^>]*\/>/)[0], 'link')]);
    const initial = frame.quaternion.clone();
    const axisTag = match[2].match(/<axis\b[^>]*\/>/)?.[0];
    const axis = new Vector3(...tuple(axisTag ? attribute(axisTag, 'xyz') : '1 0 0')).normalize();
    const setAngle = angle => {
      frame.quaternion.setFromAxisAngle(axis, angle).premultiply(initial);
      setAngle.angle = angle;
    };
    setAngle.angle = 0;
    setAngle.setJointValue = setAngle;
    const limit = match[2].match(/<limit\b[^>]*\/>/)?.[0];
    if (limit) setAngle.limit = { lower:Number(attribute(limit,'lower')), upper:Number(attribute(limit,'upper')) };
    joints[name] = setAngle;
  }
  const robot = links.base_link;
  robot.links = links;
  robot.joints = joints;
  robot.rotation.x = -Math.PI / 2;
  return { robot, links, joints };
}

