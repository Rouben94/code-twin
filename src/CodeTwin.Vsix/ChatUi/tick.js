"use strict";

// Tick, the CodeTwin mascot. The grid is the single source of truth; every rendering is generated from it.
const TickSprite = (() => {
  const grid = [
    ".B......B........",
    ".BB....BB........",
    ".BPBBBBPB........",
    ".BBBBBBBB....GGG.",
    ".BEEBBEEB....CCC.",
    ".BEEBBEEB....CCC.",
    ".BBBPPBBB.....K..",
    "..BBBBBB......K..",
    "..BBBBBBBB....K..",
    "..BBBBBBBBKKKKK..",
    "..BBBBBBBB.......",
    "..B.B..B.B.......",
  ];

  const colors = { B: "#4A5361", P: "#E8919A", K: "#8C949F", C: "#C9CED6", G: "#D9A93A", closed: "#2B313A", F: "#101317", L: "#1F242C", H: "#A9B6C6" };
  const eyeColors = { run: "#3BD16F", warn: "#F2B33D", error: "#F0524F" };
  // Sunglasses for private chats: F frame, L lens, H glint; E keeps a tiny LED reflection so the state stays visible.
  const shades = {
    4: ".FHLFFHLF........",
    5: "..LE..LE.........",
  };
  const width = grid[0].length;
  const height = grid.length;
  const canvases = new Set();
  const poses = new WeakMap();
  const steppingLegs = "...BB...BB.......";
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  let state = "run";
  let eyesLit = true;
  let blinkTimer = 0;
  let wearingShades = false;

  const colorOf = (cell) => (cell === "E" ? (eyesLit ? eyeColors[state] : wearingShades ? colors.L : colors.B) : colors[cell]);

  // Poses are derived from the grid: "step" swaps the leg row, "blink" and "sleep" close the eyes.
  const cellAt = (pose, x, y) => {
    if (pose === "step" && y === height - 1) {
      return steppingLegs[x];
    }
    const shade = wearingShades && shades[y] ? shades[y][x] : ".";
    if (shade !== ".") {
      return shade === "E" && (pose === "blink" || pose === "sleep") ? "L" : shade;
    }
    const cell = grid[y][x];
    if (cell === "E" && (pose === "blink" || pose === "sleep")) {
      return grid[y + 1][x] === "E" ? "B" : "closed";
    }
    return cell;
  };

  const draw = (canvas) => {
    const context = canvas.getContext("2d");
    const pose = poses.get(canvas);
    context.clearRect(0, 0, width, height);
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const color = colorOf(cellAt(pose, x, y));
        if (color) {
          context.fillStyle = color;
          context.fillRect(x, y, 1, 1);
        }
      }
    }
  };

  const render = () => {
    for (const canvas of canvases) {
      if (canvas.isConnected) {
        draw(canvas);
      } else {
        canvases.delete(canvas);
      }
    }
  };

  // One canvas pixel per cell, scaled up by an integer factor with nearest-neighbour sampling.
  const create = (scale, className) => {
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    canvas.className = `tick ${className || ""}`.trim();
    canvas.style.width = `${width * scale}px`;
    canvas.style.height = `${height * scale}px`;
    canvas.setAttribute("role", "img");
    canvas.setAttribute("aria-label", "Tick");
    canvases.add(canvas);
    draw(canvas);
    return canvas;
  };

  const setState = (next) => {
    if (!(next in eyeColors) || next === state) {
      return;
    }
    state = next;
    clearInterval(blinkTimer);
    eyesLit = true;
    if (state === "warn" && !reducedMotion) {
      blinkTimer = setInterval(() => {
        eyesLit = !eyesLit;
        render();
      }, 500);
    }
    render();
  };

  // Unicode half blocks, two sprite rows per text line, for places that only show text.
  const toText = () => {
    const filled = (y, x) => y < height && grid[y][x] !== ".";
    const lines = [];
    for (let y = 0; y < height; y += 2) {
      let line = "";
      for (let x = 0; x < width; x++) {
        const top = filled(y, x);
        const bottom = filled(y + 1, x);
        line += top && bottom ? "\u2588" : top ? "\u2580" : bottom ? "\u2584" : " ";
      }
      lines.push(line.trimEnd());
    }
    return lines.join("\n");
  };

  const setPose = (canvas, pose) => {
    poses.set(canvas, pose);
    draw(canvas);
  };

  const setShades = (on) => {
    wearingShades = Boolean(on);
    render();
  };

  return { create, setState, setPose, setShades, toText, width, height, eyeColor: (name) => eyeColors[name || state], get state() { return state; } };
})();

function setTickState(state) {
  TickSprite.setState(state);
}
