const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawn } = require("node:child_process");

const edge = "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
if (!fs.existsSync(edge)) throw new Error("Microsoft Edge is required for the responsive feed-header gate.");
const profile = fs.mkdtempSync(path.join(os.tmpdir(), "socyvia-edge-"));
const port = 9327;
const browser = spawn(edge, ["--headless=new", "--disable-gpu", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, "about:blank"], { stdio: "ignore" });

const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
async function json(url) {
  for (let attempt = 0; attempt < 40; attempt++) {
    try { return await (await fetch(url)).json(); } catch { await wait(125); }
  }
  throw new Error("Edge DevTools endpoint did not start.");
}

async function run() {
  const pages = await json(`http://127.0.0.1:${port}/json`);
  const page = pages.find(item => item.type === "page") || pages[0];
  const socket = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let id = 0;
  const pending = new Map();
  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (!message.id || !pending.has(message.id)) return;
    const { resolve, reject } = pending.get(message.id); pending.delete(message.id);
    message.error ? reject(new Error(message.error.message)) : resolve(message.result);
  };
  const command = (method, params = {}) => new Promise((resolve, reject) => {
    const requestId = ++id; pending.set(requestId, { resolve, reject });
    socket.send(JSON.stringify({ id: requestId, method, params }));
  });
  const evaluate = async expression => {
    const result = await command("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
    return result.result.value;
  };
  await command("Page.enable"); await command("Runtime.enable");
  await command("Page.navigate", { url: "https://socyvia.com/experimentfeed/demo" });
  let ready = false;
  for (let attempt = 0; attempt < 30; attempt++) {
    await wait(100);
    ready = await evaluate(`!!document.querySelector('[data-explore]')`);
    if (ready) break;
  }
  assert.ok(ready, "the deployed rich Demo landing loaded before header QA");
  await evaluate(`document.querySelector('[data-explore]')?.click();true`);
  await wait(150);
  const persistentPost = await evaluate(`(()=>{const like=document.querySelector('[data-like]');if(!like)return null;const post=like.dataset.like;like.click();document.querySelector('[data-save="'+post+'"]')?.click();document.querySelector('[data-comments="'+post+'"]')?.click();const input=document.querySelector('.comments input');if(input){input.value='SOCYVIA language-state QA';input.closest('form')?.requestSubmit()}return post})()`);
  assert.ok(persistentPost, "the deployed rich Demo exposes stateful interactions before language switching");
  await wait(80);

  for (const width of [1440, 1024, 768, 390]) {
    await command("Emulation.setDeviceMetricsOverride", { width, height: 900, deviceScaleFactor: 1, mobile: false });
    for (const language of ["en", "ar"]) {
      await evaluate(`document.querySelector('[data-lang="${language}"]').click()`);
      await wait(80);
      const metrics = await evaluate(`(()=>{const h=document.querySelector('.feed-header'),b=h.querySelector('.brand'),logo=b.querySelector('img').getBoundingClientRect(),wordmark=b.querySelector('span').getBoundingClientRect(),cs=getComputedStyle(h),hr=h.getBoundingClientRect(),br=b.getBoundingClientRect(),children=[...h.children].map(e=>{const r=e.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,width:r.width,height:r.height}}),overlaps=[];for(let i=0;i<children.length;i++)for(let j=i+1;j<children.length;j++){const a=children[i],c=children[j];if(Math.min(a.right,c.right)-Math.max(a.left,c.left)>.5&&Math.min(a.bottom,c.bottom)-Math.max(a.top,c.top)>.5)overlaps.push([i,j])}const start=parseFloat(cs.paddingInlineStart)||0,rtl=document.documentElement.dir==='rtl',edgeDelta=rtl?Math.abs(br.right-(hr.right-start)):Math.abs(br.left-(hr.left+start)),logoFirst=rtl?logo.right>wordmark.right:logo.left<wordmark.left,interactionState=!!document.querySelector('[data-like="${persistentPost}"].active')&&!!document.querySelector('[data-save="${persistentPost}"].active')&&document.body.textContent.includes('SOCYVIA language-state QA');return {dir:document.documentElement.dir,edgeDelta,logoFirst,interactionState,overlaps,documentWidth:document.documentElement.scrollWidth,viewport:innerWidth,brandWidth:br.width,headerHeight:hr.height}})()`);
      assert.equal(metrics.dir, language === "ar" ? "rtl" : "ltr", `${width}px ${language} document direction`);
      assert.ok(metrics.edgeDelta <= 1.5, `${width}px ${language} brand is anchored to the logical start edge (delta ${metrics.edgeDelta})`);
      assert.ok(metrics.logoFirst, `${width}px ${language} real logo precedes the wordmark from the logical start edge`);
      assert.ok(metrics.interactionState, `${width}px ${language} switching language preserves likes, saves, and participant comments`);
      assert.deepEqual(metrics.overlaps, [], `${width}px ${language} header children do not overlap`);
      assert.ok(metrics.documentWidth <= metrics.viewport + 1, `${width}px ${language} header creates no horizontal overflow`);
      assert.ok(metrics.brandWidth > 90, `${width}px ${language} brand remains visually primary`);
    }
  }
  socket.close();
  console.log("Responsive RTL/LTR Experiment Feed header browser tests passed (1440/1024/768/390).");
}

run().finally(async () => {
  browser.kill();
  await wait(800);
  try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* Edge may release its disposable profile after process exit. */ }
}).catch(error => { console.error(error); process.exitCode = 1; });
