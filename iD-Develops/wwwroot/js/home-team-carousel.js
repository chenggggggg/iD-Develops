(function () {
  function initCarousel(carousel) {
    var track = carousel.querySelector("[data-team-track]");
    var profiles = Array.prototype.slice.call(carousel.querySelectorAll("[data-team-profile]"));
    var indicators = Array.prototype.slice.call(carousel.querySelectorAll("[data-team-indicator]"));
    var activeIndex = 0;
    var scrollFrame = null;

    if (!track || profiles.length === 0 || indicators.length !== profiles.length) {
      return;
    }

    function setActive(index) {
      activeIndex = Math.max(0, Math.min(index, profiles.length - 1));

      profiles.forEach(function (profile, profileIndex) {
        profile.setAttribute("aria-hidden", profileIndex === activeIndex ? "false" : "true");
      });

      indicators.forEach(function (indicator, indicatorIndex) {
        indicator.setAttribute("aria-selected", indicatorIndex === activeIndex ? "true" : "false");
      });
    }

    function closestProfileIndex() {
      var closestIndex = 0;
      var closestDistance = Number.POSITIVE_INFINITY;

      profiles.forEach(function (profile, index) {
        var distance = Math.abs(profile.offsetLeft - track.scrollLeft);

        if (distance < closestDistance) {
          closestDistance = distance;
          closestIndex = index;
        }
      });

      return closestIndex;
    }

    indicators.forEach(function (indicator, index) {
      indicator.addEventListener("click", function () {
        track.scrollTo({
          left: profiles[index].offsetLeft,
          behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth"
        });
        setActive(index);
      });
    });

    track.addEventListener("scroll", function () {
      if (scrollFrame) {
        window.cancelAnimationFrame(scrollFrame);
      }

      scrollFrame = window.requestAnimationFrame(function () {
        setActive(closestProfileIndex());
      });
    }, { passive: true });

    setActive(0);
  }

  function initAll() {
    Array.prototype.slice.call(document.querySelectorAll("[data-team-carousel]")).forEach(initCarousel);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initAll);
  } else {
    initAll();
  }
})();
