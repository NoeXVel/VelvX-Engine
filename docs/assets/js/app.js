(function () {
  "use strict";

  var REPO = "https://github.com/NoeXVel/VelvX-Engine";

  // ----- Markdown doc viewer -----
  var body = document.querySelector("[data-doc]");
  if (body) {
    var file = body.getAttribute("data-doc");
    var side = document.querySelector(".doc-side");
    if (side) {
      var links = side.querySelectorAll("a[data-page]");
      for (var i = 0; i < links.length; i++) {
        if (links[i].getAttribute("data-page") === file) links[i].classList.add("active");
      }
    }
    fetch(file)
      .then(function (r) {
        if (!r.ok) throw new Error("HTTP " + r.status);
        return r.text();
      })
      .then(function (md) {
        if (window.marked) {
          body.innerHTML = window.marked.parse(md);
        } else {
          body.innerHTML = "<pre>" + md.replace(/&/g, "&amp;").replace(/</g, "&lt;") + "</pre>";
        }
        body.classList.remove("doc-loading");
        fixDocLinks(body);
      })
      .catch(function (e) {
        body.classList.remove("doc-loading");
        body.innerHTML =
          '<p>Não foi possível carregar <code>' + file + "</code> (" + e.message +
          '). Leia direto no repositório: <a href="' + REPO + "/blob/main/docs/" + file + '">' + file + "</a>.</p>";
      });
  }

  // Rewrite relative .md links inside rendered docs to the site viewer pages.
  function fixDocLinks(root) {
    var map = {
      "GettingStarted.md": "guide.html",
      "VelvLanguage.md": "language.html",
      "Architecture.md": "architecture.html"
    };
    var as = root.querySelectorAll("a[href]");
    for (var i = 0; i < as.length; i++) {
      var href = as[i].getAttribute("href");
      var base = href.split("/").pop();
      if (map[base]) as[i].setAttribute("href", map[base]);
    }
  }

  // ----- Lightbox for gallery -----
  var lb = document.getElementById("lightbox");
  if (lb) {
    var lbImg = lb.querySelector("img");
    var shots = document.querySelectorAll(".shot img");
    for (var s = 0; s < shots.length; s++) {
      shots[s].addEventListener("click", function () {
        lbImg.src = this.src;
        lbImg.alt = this.alt;
        lb.classList.add("open");
      });
    }
    lb.addEventListener("click", function (ev) {
      if (ev.target === lb || ev.target.classList.contains("close")) lb.classList.remove("open");
    });
    document.addEventListener("keydown", function (ev) {
      if (ev.key === "Escape") lb.classList.remove("open");
    });
  }

  // ----- Mark active nav link -----
  var path = location.pathname.split("/").pop() || "index.html";
  var nav = document.querySelectorAll(".nav-links a[href]");
  for (var n = 0; n < nav.length; n++) {
    if (nav[n].getAttribute("href") === path) nav[n].classList.add("active");
  }
})();