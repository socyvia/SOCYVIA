const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const app = fs.readFileSync(path.join(root, "WebExperimentFeed", "app.js"), "utf8");
const styles = fs.readFileSync(path.join(root, "WebExperimentFeed", "styles.css"), "utf8");
const html = fs.readFileSync(path.join(root, "WebExperimentFeed", "index.html"), "utf8");
const shell = fs.readFileSync(path.join(root, "WebExperimentFeed", "feed-shell.js"), "utf8");

assert.ok(app.includes('if(!/^\\/experimentfeed\\/demo\\/?$/.test(location.pathname))'), "the rich product Demo is isolated to the public demo route");
assert.match(app, /window\.SocyviaParticipantFlow\?\.start\(\)/, "non-demo Preview and Live routes still use the participant runtime");
assert.doesNotMatch(app, /\bfetch\s*\(|XMLHttpRequest|sendBeacon/, "the public Demo performs no research or telemetry network writes");

for (const identity of ["Abdellah R.", "Sara M.", "Youssef A.", "Lina K.", "SOCYVIA"])
  assert.ok(app.includes(identity), `the restored Demo retains ${identity}`);

for (const post of ["sport", "economy", "culture", "technology", "society", "audio", "civic", "source", "urban", "variation"])
  assert.ok(app.includes(`id:\"${post}\"`), `the restored Demo includes the ${post} post`);

for (const capability of ["gallery:[", "video:", "audio:", "source:{", "data-comment-form", "data-restart", "data-expand", "data-like", "data-save", "data-share"])
  assert.ok(app.includes(capability), `the restored Demo includes ${capability}`);

assert.ok(app.includes('target="_blank" rel="noopener noreferrer"'), "external sources open safely in a new tab");
assert.ok(app.includes('e.key==="Escape"'), "Demo modals support Escape");
assert.ok(app.includes('document.documentElement.dir=rtl()?"rtl":"ltr"'), "language switching updates true document direction");
const demoHeader = app.match(/const header=\(\)=>`([^`]+)`;/)?.[1] || "";
assert.ok(demoHeader.indexOf('class="brand"') < demoHeader.indexOf("researcherIdentity()"), "Demo header starts with the brand semantically");
assert.ok(demoHeader.indexOf("researcherIdentity()") < demoHeader.indexOf("feed-notice"), "researcher identity follows the brand");
assert.ok(demoHeader.indexOf("feed-notice") < demoHeader.indexOf("language()"), "language is the logical-end control");
assert.doesNotMatch(demoHeader, /header-actions/, "the direction-aware header has no wrapper that can displace the brand");
assert.match(styles, /\.feed-header>\.language,.feed-header>\.participant-language\{[^}]*margin-inline-start:auto/, "the language control owns logical-end spacing");
assert.match(styles, /grid-template-areas:"brand language" "researcher notice"/, "narrow headers wrap without changing semantic direction");
assert.match(styles, /\.feed\s*\{[^}]*width:min\(100% - 32px,720px\)/s, "the restored feed retains its readable central width");
assert.match(html, /<title>SOCYVIA Experiment Feed<\/title>/, "the browser tab retains the approved SOCYVIA Demo title");
assert.match(html, /rel="icon"[^>]+\/experimentfeed\/demo-media\/socyvia-mark\.png/, "the mounted Demo references the real SOCYVIA favicon");
assert.match(html, /rel="shortcut icon"[^>]+\/experimentfeed\/demo-media\/socyvia-mark\.png/, "the Demo declares a compatible shortcut icon");
assert.ok(html.includes("feed-shell.js") && html.includes("feed-shell.css"), "Demo and Live load the shared rich participant feed shell");
assert.ok(shell.includes('const version = "SOCYVIA.RichFeed/1"') && app.includes("FEED_SHELL.adopt"), "the restored Demo is locked to the shared production feed-shell identity");

for (const asset of [
  "sport-supporters.jpg", "local-commerce.jpg", "gallery-exhibition.jpg",
  "gallery-library.jpg", "gallery-media-lab.jpg", "socyvia-demo-video.mp4",
  "information-current.png", "socyvia-demo-tone.mp3", "urban-courtyard.jpg",
  "gallery-mobility.jpg", "civic-rhythm.png", "socyvia-mark.png"
])
  assert.ok(fs.existsSync(path.join(root, "WebExperimentFeed", "demo-media", asset)), `Demo asset ${asset} is bundled`);

console.log("Rich isolated SOCYVIA public Demo regression tests passed");
