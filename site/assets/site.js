(() => {
  const search = document.querySelector(".search");
  const links = Array.from(document.querySelectorAll(".nav a"));

  if (search) {
    search.addEventListener("input", () => {
      const query = search.value.trim().toLowerCase();
      for (const link of links) {
        link.hidden = Boolean(query) && !link.textContent.toLowerCase().includes(query);
      }
    });
  }

  const repository = window.SLEDDERS_LUA_REPOSITORY || "";
  for (const link of document.querySelectorAll("[data-repo-path]")) {
    if (!repository) {
      link.hidden = true;
      continue;
    }
    link.href = `https://github.com/${repository}/${link.dataset.repoPath}`;
  }
})();
