import { acceptsMessage } from "./demos/studio-core.mjs";

const dialog = document.getElementById("demo-dialog");
const host = document.getElementById("runtime-host");
const status = document.getElementById("demo-status");
const title = document.getElementById("demo-title");
let active = null;
let opener = null;
let loadingTimer;

function startDemo(slug, label, framework = "wpf") {
  clearTimeout(loadingTimer);
  host.replaceChildren();
  active = { slug, label, framework, session: crypto.randomUUID() };
  title.textContent = label;
  document.getElementById("demo-framework").textContent =
    framework === "winforms" ? "WinForms / WebAssembly" : "WPF / WebAssembly";
  status.textContent =
    "Starting the app. The first launch downloads the shared .NET runtime…";
  status.dataset.state = "loading";
  const frame = document.createElement("iframe");
  frame.title = label;
  const directory = framework === "winforms" ? "winforms" : "demos";
  frame.src = `${directory}/?${new URLSearchParams({ demo: slug, session: active.session, renderer: "auto" })}`;
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
    startDemo(link.dataset.demo, link.dataset.title, link.dataset.framework);
  });
}

document
  .getElementById("close-demo")
  .addEventListener("click", () => dialog.close());
document.getElementById("restart-demo").addEventListener("click", () => {
  if (active) startDemo(active.slug, active.label, active.framework);
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
  const winformsMessage =
    active?.framework === "winforms" &&
    event.origin === location.origin &&
    event.source === frame?.contentWindow &&
    event.data?.channel === "wfwasm-runtime-v1" &&
    event.data.app === active.slug &&
    event.data.session === active.session;
  if (
    !active ||
    !(
      winformsMessage ||
      (active.framework !== "winforms" &&
        acceptsMessage(event, {
          origin: location.origin,
          source: frame?.contentWindow,
          session: active.session,
          slug: active.slug,
        }))
    )
  )
    return;
  if (event.data.type === "ready") {
    clearTimeout(loadingTimer);
    status.dataset.state = "ready";
    status.textContent = "Running in your browser. Give it a try.";
  } else if (event.data.type === "error" || event.data.type === "failed") {
    clearTimeout(loadingTimer);
    status.dataset.state = "error";
    status.textContent = `The app encountered an error. ${event.data.message}`;
  }
});
