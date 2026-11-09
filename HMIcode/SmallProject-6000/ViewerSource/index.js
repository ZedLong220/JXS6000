import {
  ACESFilmicToneMapping,
  AmbientLight,
  AxesHelper,
  Box3,
  Color,
  DirectionalLight,
  GridHelper,
  LoadingManager,
  MathUtils,
  Matrix4,
  PerspectiveCamera,
  Scene,
  Vector3,
  WebGLRenderer,
} from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import URDFLoader from 'urdf-loader';
import { setupRobotControls, setAppStatus } from './robotControls.js';
import { setupHmiBridge } from './hmiBridge.js';

const scene = new Scene();
scene.background = new Color(0x171a1d);
const MODEL_VERSION = '20261106-jxs6000-stl-v1';

const camera = new PerspectiveCamera(38, 1, 0.01, 100);
camera.position.set(1.8, 1.35, 1.8);

const renderer = new WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.toneMapping = ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.15;
document.getElementById('viewport').appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.06;
controls.minDistance = 0.35;
controls.maxDistance = 8;
controls.target.set(0, 0.55, 0);

scene.add(new AmbientLight(0xffffff, 1.4));

const keyLight = new DirectionalLight(0xffffff, 3.2);
keyLight.position.set(3, 5, 3);
keyLight.castShadow = true;
keyLight.shadow.mapSize.set(2048, 2048);
keyLight.shadow.camera.left = -2;
keyLight.shadow.camera.right = 2;
keyLight.shadow.camera.top = 2;
keyLight.shadow.camera.bottom = -2;
scene.add(keyLight);

const fillLight = new DirectionalLight(0xb9d7ff, 1.8);
fillLight.position.set(-4, 2, -3);
scene.add(fillLight);

const grid = new GridHelper(8, 80, 0x596168, 0x2f353a);
grid.material.opacity = 0.48;
grid.material.transparent = true;
scene.add(grid);

const axes = new AxesHelper(0.22);
axes.position.y = 0.002;
scene.add(axes);

let robot = null;
let updateRobotControls = () => {};
let updateHmi = () => {};
let initialCamera = null;

function fitCameraToRobot() {
  const bounds = new Box3().setFromObject(robot);
  const size = bounds.getSize(new Vector3());
  const center = bounds.getCenter(new Vector3());
  const radius = Math.max(size.x, size.y, size.z) * 0.72;

  controls.target.copy(center);
  camera.position.copy(center).add(new Vector3(radius * 1.9, radius * 1.35, radius * 1.9));
  camera.near = Math.max(radius / 100, 0.005);
  camera.far = Math.max(radius * 30, 20);
  camera.updateProjectionMatrix();
  controls.update();

  initialCamera = {
    position: camera.position.clone(),
    target: controls.target.clone(),
  };
}

function resetView() {
  if (!initialCamera) return;
  camera.position.copy(initialCamera.position);
  controls.target.copy(initialCamera.target);
  controls.update();
}

function loadRobot() {
  setAppStatus('loading', '正在加载模型');

  const manager = new LoadingManager();
  manager.setURLModifier(url => {
    const separator = url.includes('?') ? '&' : '?';
    return `${url}${separator}v=${MODEL_VERSION}`;
  });
  const loader = new URDFLoader(manager);
  loader.parseCollision = true;
  loader.packages = {
    jxs6000_description: '/URDF/jxs6000_description',
  };
  let modelPrepared = false;
  let loadFailed = false;

  manager.onError = url => {
    loadFailed = true;
    console.error('模型资源加载失败:', url);
    setAppStatus('error', '模型资源加载失败，请刷新重试');
  };

  manager.onLoad = () => {
    if (!robot || modelPrepared || loadFailed) return;
    modelPrepared = true;

    robot.updateMatrixWorld(true);
    const bounds = new Box3().setFromObject(robot);
    if (!Number.isFinite(bounds.min.y)) {
      setAppStatus('error', '模型包围盒无效');
      return;
    }

    // The URDF already places the stationary S1 mounting plane at Z=0.
    // Preserve its base frame instead of translating by a moving-link bound.
    scene.add(robot);
    robot.updateMatrixWorld(true);

    fitCameraToRobot();
    setAppStatus('ready', '模型已就绪');
    updateRobotControls = setupRobotControls(robot);
    updateHmi = setupHmiBridge(robot, updateRobotControls);
  };

  loader.load(
    '/URDF/jxs6000.urdf',
    result => {
      robot = result;
      window.robot = robot;

      // URDF uses Z-up; Three.js uses Y-up.
      robot.rotation.x = -Math.PI / 2;
      // S6 is the last motor housing. Its output flange has no separate STL;
      // show the tool axes so J6 rotation still has a visible reference.
      robot.links.tool0.add(new AxesHelper(0.055));
      robot.traverse(object => {
        if (object.isURDFCollider) object.visible = false;
        object.castShadow = true;
        object.receiveShadow = true;
      });
    },
    undefined,
    error => {
      console.error(error);
      setAppStatus('error', '模型加载失败');
    },
  );
}

function updateEndPose() {
  if (!robot) return;
  const baseLink = robot.getObjectByName('base_link');
  const endLink = robot.links.tool0;
  if (!baseLink || !endLink) return;

  robot.updateMatrixWorld(true);
  const baseInverse = new Matrix4().copy(baseLink.matrixWorld).invert();
  const relative = new Matrix4().multiplyMatrices(baseInverse, endLink.matrixWorld);
  const position = new Vector3().setFromMatrixPosition(relative);

  const xAxis = new Vector3().setFromMatrixColumn(relative, 0);
  const yAxis = new Vector3().setFromMatrixColumn(relative, 1);
  const zAxis = new Vector3().setFromMatrixColumn(relative, 2);
  const roll = Math.atan2(yAxis.z, zAxis.z);
  const pitch = Math.atan2(-xAxis.z, Math.hypot(yAxis.z, zAxis.z));
  const yaw = Math.atan2(xAxis.y, xAxis.x);

  const values = {
    posX: position.x.toFixed(3),
    posY: position.y.toFixed(3),
    posZ: position.z.toFixed(3),
    rotR: MathUtils.radToDeg(roll).toFixed(1),
    rotP: MathUtils.radToDeg(pitch).toFixed(1),
    rotY: MathUtils.radToDeg(yaw).toFixed(1),
  };
  Object.entries(values).forEach(([id, value]) => {
    const element = document.getElementById(id);
    if (element) element.textContent = value;
  });
}

function resize() {
  const viewport = document.getElementById('viewport');
  const width = viewport.clientWidth;
  const height = viewport.clientHeight;
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

const panel = document.getElementById('controlPanel');
const panelToggle = document.getElementById('panelToggle');
const mobileLayout = window.matchMedia('(max-width: 720px)');

function setPanelOpen(open) {
  panel.classList.toggle('is-collapsed', !open);
  panelToggle.setAttribute('aria-expanded', String(open));
  panelToggle.textContent = open ? '收起' : '控制';
}

function syncPanelLayout(event) {
  setPanelOpen(!event.matches);
}

panelToggle.addEventListener('click', () => {
  setPanelOpen(panel.classList.contains('is-collapsed'));
});
mobileLayout.addEventListener('change', syncPanelLayout);
document.getElementById('resetView').addEventListener('click', resetView);
window.addEventListener('resize', resize);

function render() {
  requestAnimationFrame(render);
  updateRobotControls();
  updateHmi();
  updateEndPose();
  controls.update();
  renderer.render(scene, camera);
}

resize();
syncPanelLayout(mobileLayout);
loadRobot();
render();
