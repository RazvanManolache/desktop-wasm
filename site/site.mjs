import { acceptsMessage } from "./demos/studio-core.mjs";

const dialog = document.getElementById("demo-dialog");
const host = document.getElementById("runtime-host");
const status = document.getElementById("demo-status");
const title = document.getElementById("demo-title");
let active = null;
let opener = null;
let loadingTimer;

function startDemo(slug, label) {
  clearTimeout(loadingTimer);
  host.replaceChildren();
  active = { slug, label, session: crypto.randomUUID() };
  title.textContent = label;
  status.textContent =
    "Starting the app. The first launch downloads the shared .NET runtime…";
  status.dataset.state = "loading";
  const frame = document.createElement("iframe");
  frame.title = label;
  frame.src = `demos/?${new URLSearchParams({ demo: slug, session: active.session, renderer: "auto" })}`;
  host.append(frame);
  loadingTimer = setTimeout(() => {
    status.textContent =
      "Still loading. The first launch can take longer on a slow connection. You can restart the app above.";
  }, 45000);
}

for (const link of document.querySelectorAll("[data-demo]")) {
  link.addEventListener("click", (event) => {
    if (
      event.button !== 0 ||
      event.ctrlKey ||
      event.metaKey ||
      event.shiftKey ||
      event.altKey
    )
      return;
    event.preventDefault();
    opener = link;
    dialog.showModal();
    document.body.style.overflow = "hidden";
    startDemo(link.dataset.demo, link.dataset.title);
  });
}

document
  .getElementById("close-demo")
  .addEventListener("click", () => dialog.close());
document.getElementById("restart-demo").addEventListener("click", () => {
  if (active) startDemo(active.slug, active.label);
});
dialog.addEventListener("close", () => {
  clearTimeout(loadingTimer);
  active = null;
  host.replaceChildren();
  document.body.style.overflow = "";
  opener?.focus({ preventScroll: true });
});

addEventListener("message", (event) => {
  const frame = host.querySelector("iframe");
  if (
    !active ||
    !acceptsMessage(event, {
      origin: location.origin,
      source: frame?.contentWindow,
      session: active.session,
      slug: active.slug,
    })
  )
    return;
  if (event.data.type === "ready") {
    clearTimeout(loadingTimer);
    status.dataset.state = "ready";
    status.textContent = "Running in your browser. Give it a try.";
  } else if (event.data.type === "error") {
    clearTimeout(loadingTimer);
    status.dataset.state = "error";
    status.textContent = `The app encountered an error. ${event.data.message}`;
  }
});
