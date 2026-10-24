"use strict";

// Tick lives behind the message box: now and then it peeks over the edge, strolls along the top,
// sits, naps, and kneads when the user types.
(() => {
  if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
    return;
  }

  const composer = document.getElementById("composer");
  const prompt = document.getElementById("prompt");
  const scale = 3;
  const spriteWidth = TickSprite.width * scale;
  const spriteHeight = TickSprite.height * scale;
  const margin = 20;
  const offsets = { hidden: spriteHeight + 2, peek: spriteHeight - 6 * scale, up: 0 };

  const stage = document.createElement("div");
  stage.className = "tick-stage";
  stage.setAttribute("aria-hidden", "true");
  const walker = stage.appendChild(document.createElement("div"));
  walker.className = "tick-walker";
  const sprite = walker.appendChild(TickSprite.create(scale));
  const zzz = walker.appendChild(document.createElement("span"));
  zzz.className = "tick-zzz";
  zzz.textContent = "z";
  composer.prepend(stage);

  let x = 0;
  let facing = -1;
  let visible = false;
  let moving = false;
  let napping = false;
  let kneading = false;
  let wake = null;

  const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
  const random = (min, max) => min + Math.random() * (max - min);
  const chance = (probability) => Math.random() < probability;
  const maxX = () => Math.max(0, stage.clientWidth - spriteWidth - 2 * margin);
  const randomX = () => Math.round(random(0, maxX()) / scale) * scale;

  const idle = (ms) => new Promise((resolve) => {
    const timer = setTimeout(resolve, ms);
    wake = () => {
      clearTimeout(timer);
      resolve();
    };
  });

  const place = () => {
    x = Math.min(x, maxX());
    walker.style.left = `${margin + x}px`;
  };

  const face = (direction) => {
    facing = direction;
    sprite.style.transform = direction > 0 ? "scaleX(-1)" : "";
  };

  const pose = (name) => TickSprite.setPose(sprite, name);

  const lift = async (offset) => {
    walker.style.transform = `translateY(${offsets[offset]}px)`;
    visible = offset === "up";
    await wait(450);
  };

  const blink = async () => {
    pose("blink");
    await wait(160);
    pose("stand");
  };

  const walkTo = async (target) => {
    moving = true;
    face(target > x ? 1 : -1);
    let step = false;
    while (Math.abs(target - x) >= scale) {
      x += facing * scale;
      step = !step;
      pose(step ? "step" : "stand");
      place();
      await wait(140);
    }
    pose("stand");
    moving = false;
  };

  const nap = async () => {
    napping = true;
    zzz.style.left = facing > 0 ? `${spriteWidth - 6 * scale}px` : `${2 * scale}px`;
    pose("sleep");
    walker.classList.add("napping");
    const end = Date.now() + random(6000, 12000);
    while (napping && Date.now() < end) {
      await wait(200);
    }
    walker.classList.remove("napping");
    napping = false;
    await blink();
    await wait(400);
  };

  const knead = async () => {
    kneading = true;
    walker.classList.add("hop");
    for (let i = 0; i < 4; i++) {
      pose(i % 2 ? "stand" : "step");
      await wait(110);
    }
    pose("stand");
    walker.classList.remove("hop");
    kneading = false;
  };

  const peek = async () => {
    x = randomX();
    place();
    face(chance(0.5) ? 1 : -1);
    await lift("peek");
    await wait(random(900, 1600));
    await blink();
    face(-facing);
    await wait(random(700, 1400));
    await blink();
    await lift("hidden");
  };

  const stroll = async () => {
    x = randomX();
    place();
    face(chance(0.5) ? 1 : -1);
    await lift("peek");
    await wait(random(400, 900));
    await lift("up");
    await walkTo(randomX());
    await wait(random(1200, 2500));
    await blink();
    if (chance(0.35)) {
      await nap();
    } else if (chance(0.5)) {
      await walkTo(randomX());
      await wait(random(800, 1600));
    }
    await lift("hidden");
  };

  const live = async () => {
    await idle(random(3000, 6000));
    for (;;) {
      if (!document.hidden && stage.clientWidth >= spriteWidth + 2 * margin) {
        await (chance(0.4) ? peek() : stroll());
      }
      await idle(random(8000, 25000));
    }
  };

  prompt.addEventListener("input", () => {
    if (napping) {
      napping = false;
    } else if (visible && !moving && !kneading) {
      knead();
    } else if (!visible && wake && chance(0.3)) {
      wake();
      wake = null;
    }
  });

  window.addEventListener("resize", place);

  walker.style.transform = `translateY(${offsets.hidden}px)`;
  pose("stand");
  live();
})();
