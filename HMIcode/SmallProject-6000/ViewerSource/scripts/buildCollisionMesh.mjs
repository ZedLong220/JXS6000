import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';

// Keep S6's four main solids; omit only its disconnected small detail from
// collision checking. The visual mesh stays byte-for-byte unchanged.
const source = new URL('../public/URDF/jxs6000_description/meshes/s6.stl', import.meta.url);
const destination = new URL('../public/URDF/jxs6000_description/collision/s6.stl', import.meta.url);
const data = readFileSync(source);
const count = data.readUInt32LE(80);
if (data.length !== 84 + count * 50) throw new Error('Expected binary S6 STL');
const parents = Array.from({ length: count }, (_, i) => i);
const find = i => {
  while (parents[i] !== i) {
    parents[i] = parents[parents[i]];
    i = parents[i];
  }
  return i;
};
const owners = new Map();
for (let face = 0; face < count; face++) {
  for (let vertex = 0; vertex < 3; vertex++) {
    const offset = 84 + face * 50 + 12 + vertex * 12;
    const key = [0, 4, 8].map(n => data.readFloatLE(offset + n)).join(',');
    if (owners.has(key)) parents[find(face)] = find(owners.get(key));
    else owners.set(key, face);
  }
}
const parts = new Map();
for (let i = 0; i < count; i++) {
  const key = find(i);
  if (!parts.has(key)) parts.set(key, []);
  parts.get(key).push(i);
}
if (parts.size === 4) {
  mkdirSync(new URL('.', destination), { recursive: true });
  writeFileSync(destination, data);
  console.log(`S6 collision mesh: ${count} triangles; source already has four clean components`);
  process.exit(0);
}
const details = [...parts.values()].filter(faces => {
  if (faces.length !== 28) return false;
  return faces.every(face => [0, 1, 2].every(vertex => {
    const offset = 84 + face * 50 + 12 + vertex * 12;
    const x = data.readFloatLE(offset), y = data.readFloatLE(offset + 4), z = data.readFloatLE(offset + 8);
    return x > -155.67 && x < -134.92 && y > -58.08 && y < -17.91 && z > 12.74 && z < 22.76;
  }));
});
if (parts.size !== 5 || details.length !== 1) {
  throw new Error('S6 source changed: review collision simplification before regenerating');
}
const removed = new Set(details[0]);
const header = Buffer.from(data.subarray(0, 84));
header.writeUInt32LE(count - removed.size, 80);
const faces = Array.from({ length: count }, (_, i) => i)
  .filter(i => !removed.has(i))
  .map(i => data.subarray(84 + i * 50, 84 + (i + 1) * 50));
mkdirSync(new URL('.', destination), { recursive: true });
writeFileSync(destination, Buffer.concat([header, ...faces]));
console.log(`S6 collision mesh: ${count} -> ${faces.length} triangles; visual unchanged`);
