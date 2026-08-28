/* Shared visual shell used by both the protected Demo dataset and published studies. */
((root) => {
  "use strict";
  const version = "SOCYVIA.RichFeed/1";
  const icons = {
    like: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 10v10H3V10h4Zm0 9h10.1a2 2 0 0 0 1.9-1.4l1.7-5.4A2 2 0 0 0 18.8 9H14l.7-3.1A2.4 2.4 0 0 0 12.4 3L7 10v9Z" fill="none"/></svg>',
    comment: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 5h16v11H9l-5 4V5Z" fill="none"/></svg>',
    share: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m14 5 5 5-5 5M19 10H9a5 5 0 0 0-5 5v4" fill="none"/></svg>',
    save: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 4h12v17l-6-4-6 4V4Z" fill="none"/></svg>',
    readMore: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 7h14M5 12h14M5 17h8" fill="none"/></svg>'
  };
  const attributes = (value) => String(value || "").trim();
  const article = ({ attributes: attrs = "", header = "", copy = "", media = "", source = "", engagement = "", toolbar = "", comments = "" }) =>
    `<article class="post real-text-post" data-socyvia-feed-shell="${version}" ${attributes(attrs)}>${header}${copy}${media}${source}${engagement}${toolbar}${comments}</article>`;
  const adopt = (html) => String(html || "").replace(
    '<article class="post"',
    `<article class="post" data-socyvia-feed-shell="${version}"`);
  const icon = (name) => icons[name] || "";
  root.SocyviaParticipantFeedShell = Object.freeze({ version, article, adopt, icon });
  if (typeof module !== "undefined" && module.exports) module.exports = root.SocyviaParticipantFeedShell;
})(typeof window !== "undefined" ? window : globalThis);
